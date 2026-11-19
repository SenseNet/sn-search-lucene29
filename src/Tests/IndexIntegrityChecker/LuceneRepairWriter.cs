using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace IndexIntegrityChecker;

// TODO(refactor): Experimental Lucene-only repair operations; extract a supported offline API after validation.
internal sealed class LuceneRepairWriter : IDisposable
{
    private readonly FSDirectory _directory;
    private IndexReader? _reader;
    private readonly IndexIdentity _identity;
    private readonly HashSet<int> _commitDocuments = [];

    // TODO(refactor): Experimental commit snapshot contract; consolidate with the supported commit API.
    internal Dictionary<string, string> OriginalUserData { get; }

    // TODO(refactor): Experimental fixed-commit reader; refactor lifetime together with offline writer support.
    internal LuceneRepairWriter(IndexIdentity identity)
    {
        _identity = identity;
        if (!System.IO.Directory.Exists(identity.Directory)) throw new DirectoryNotFoundException("The captured index directory is missing.");
        _directory = FSDirectory.Open(new DirectoryInfo(identity.Directory));
        try
        {
            _reader = IndexReader.Open(_directory, true);
            ValidateCommit();
            OriginalUserData = new(_reader.GetCommitUserData() ?? new Dictionary<string, string>());
            var docs = _reader.TermDocs(new Term("$#COMMIT", "$#COMMIT"));
            try { while (docs.Next()) _commitDocuments.Add(docs.Doc()); }
            finally { docs.Close(); }
        }
        catch { _reader?.Dispose(); _directory.Dispose(); throw; }
    }

    // TODO(refactor): Experimental source validation; integrate with supported offline index identity handling.
    internal void ValidateCommit()
    {
        if (IndexReader.GetCurrentVersion(_directory) != _identity.CommitVersion)
            throw new InvalidDataException("The index commit changed. Run a new full check.");
        if (IndexWriter.IsLocked(_directory)) throw new IOException("The index is locked by another writer.");
    }

    // TODO(refactor): Experimental document/version closure; extract malformed-document recovery after validation.
    internal void Expand(HashSet<int> documents, HashSet<int> versions, CancellationToken cancel)
    {
        if (documents.Count == 0 && versions.Count == 0) return;
        var reader = _reader ?? throw new ObjectDisposedException(nameof(LuceneRepairWriter));
        var expanded = new HashSet<int>();
        while (true)
        {
            foreach (var version in versions.Except(expanded).ToArray())
            {
                cancel.ThrowIfCancellationRequested();
                var matches = reader.TermDocs(new Term("VersionId", NumericUtils.IntToPrefixCoded(version)));
                try { while (matches.Next()) documents.Add(matches.Doc()); }
                finally { matches.Close(); }
                expanded.Add(version);
            }
            foreach (var doc in documents)
                if (doc < 0 || doc >= reader.MaxDoc() || reader.IsDeleted(doc) || _commitDocuments.Contains(doc))
                    throw new InvalidDataException($"Report references an invalid content DocId: {doc}.");
            var first = NumericUtils.IntToPrefixCoded(int.MinValue);
            var terms = reader.Terms(new Term("VersionId", first));
            var postings = reader.TermDocs();
            try
            {
                do
                {
                    cancel.ThrowIfCancellationRequested();
                    var term = terms.Term();
                    if (term == null || term.Field() != "VersionId" || term.Text().Length == 0 || term.Text()[0] != first[0]) break;
                    int version;
                    try { version = NumericUtils.PrefixCodedToInt(term.Text()); }
                    catch (FormatException) { continue; }
                    if (version <= 0) continue;
                    postings.Seek(terms);
                    while (postings.Next())
                        if (documents.Contains(postings.Doc())) versions.Add(version);
                } while (terms.Next());
            }
            finally { postings.Close(); terms.Close(); }
            if (versions.Count == expanded.Count) break;
        }
    }

    // TODO(refactor): Experimental two-phase repair writer; expose a supported no-resume mutation API later.
    internal async Task ApplyAsync(IReadOnlyCollection<int> deletes, IReadOnlyCollection<int> versions,
        Func<int, CancellationToken, Task<Document>> loadDocument, RepairSchema schema,
        Func<CancellationToken, Task> validateActivity, int lastActivity, CancellationToken cancel,
        Action<string>? progress = null)
    {
        ValidateCommit();
        _reader!.Dispose(); _reader = null;
        if (deletes.Count != 0)
        {
            using var reader = IndexReader.Open(_directory, false);
            if (reader.GetVersion() != _identity.CommitVersion) throw new InvalidDataException("The index commit changed before deletion.");
            foreach (var doc in deletes.Order()) { cancel.ThrowIfCancellationRequested(); reader.DeleteDocument(doc); }
            reader.Commit(OriginalUserData);
            progress?.Invoke("deletions_committed");
        }
        // Never use an original DocId after opening the writer: merges may renumber documents.
        using var fallback = new KeywordAnalyzer();
        var writer = new IndexWriter(_directory, fallback, false, IndexWriter.MaxFieldLength.UNLIMITED);
        var closed = false;
        try
        {
            foreach (var version in versions.Order())
            {
                cancel.ThrowIfCancellationRequested();
                var document = await loadDocument(version, cancel);
                using var analyzer = schema.CreateAnalyzer(document.GetFields().Cast<Fieldable>().Select(f => f.Name()));
                writer.UpdateDocument(new Term("VersionId", NumericUtils.IntToPrefixCoded(version)), document, analyzer);
                progress?.Invoke("version_written");
            }
            await validateActivity(cancel);
            cancel.ThrowIfCancellationRequested();
            var data = new Dictionary<string, string>(OriginalUserData)
            { ["LastActivityId"] = lastActivity.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            data.Remove("Gaps");
            ForceCommit(writer, data);
            progress?.Invoke("final_committed");
            writer.Close(); closed = true;
        }
        finally
        {
            // Close() can commit. On failure discard pending writes, never publish a success status.
            if (!closed) writer.Rollback();
        }
    }

    // TODO(refactor): Experimental copy of sn-search-lucene29@a90399d7599b090c92781dedefa3c9fd657adaf6
    // src/SenseNet.Search.Lucene29/LuceneSearchManager.cs::Commit/GetFakeDocument.
    // Adaptation: explicit writer/user-data, no manager lifecycle; publish a supported shared primitive later.
    internal static void ForceCommit(IndexWriter writer, IDictionary<string, string> data)
    {
        var marker = new Document();
        marker.Add(new Field("$#COMMIT", "$#COMMIT", Field.Store.YES, Field.Index.NOT_ANALYZED, Field.TermVector.NO));
        marker.Add(new Field("$#DATA", Guid.NewGuid().ToString(), Field.Store.YES, Field.Index.NOT_ANALYZED, Field.TermVector.NO));
        writer.UpdateDocument(new Term("$#COMMIT", "$#COMMIT"), marker);
        writer.Commit(data);
    }

    // TODO(refactor): Experimental resource lifetime; consolidate with a supported offline index session.
    public void Dispose() { _reader?.Dispose(); _directory.Dispose(); }
}
