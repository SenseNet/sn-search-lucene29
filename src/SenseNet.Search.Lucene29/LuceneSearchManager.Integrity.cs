#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Lucene.Net.Index;
using SenseNet.Search.Indexing;
using SenseNet.Search.Indexing.Integrity;

namespace SenseNet.Search.Lucene29;

public partial class LuceneSearchManager
{
    private long _integrityRevision;
    private bool _trackIntegrityRevision;

    private void MarkIntegrityMutation()
    {
        if (_trackIntegrityRevision)
            Interlocked.Increment(ref _integrityRevision);
    }

    public IIndexIntegrityReader OpenIntegrityReader(CancellationToken cancellationToken)
    {
        _catchUpLock.EnterReadLock();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireIntegrityWriter();
            var snapshot = new IndexSnapshot(_snapshotMaker);
            try
            {
                return new CommittedIntegrityReader(IndexReader.Open((IndexCommit)snapshot.CommitPoint, true), snapshot);
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }
        finally { _catchUpLock.ExitReadLock(); }
    }

    public IndexIntegrityProbe ProbeIntegrity(IReadOnlyCollection<int> versionIds, string scratchDirectory,
        CancellationToken cancellationToken)
    {
        IndexReader reader;
        long revision;
        _catchUpLock.EnterWriteLock();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireIntegrityWriter();
            _trackIntegrityRevision = true;
            revision = _integrityRevision;
            reader = _writer.GetReader();
        }
        finally { _catchUpLock.ExitWriteLock(); }

        using (reader)
        {
            var ids = versionIds.ToHashSet();
            var candidateDocs = new HashSet<int>();
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var postings = reader.TermDocs(GetVersionIdTerm(id));
                try { while (postings.Next()) candidateDocs.Add(postings.Doc()); }
                finally { postings.Close(); }
            }
            var issues = new List<Difference>();
            var differences = new DifferenceReporter(issue =>
            {
                if (issue.DocId is int doc && candidateDocs.Contains(doc))
                    issues.Add(issue);
            });
            using var scanner = new Integrity.LuceneSnapshot(reader, differences, cancellationToken,
                Path.Combine(scratchDirectory, "probe.map"), identifyVersions: true);
            var documents = scanner.ReadVersions().Where(row => ids.Contains(row.VersionId))
                .Select(row => new SavedIndexVersion(row.VersionId, row.DocId, scanner.GetMetadata(row.DocId))).ToArray();
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!scanner.HasStoredPath(document.DocId))
                    issues.Add(new(DifferenceKind.MissingField, DocId: document.DocId, Field: "Path"));
            }
            return new(revision, documents, issues.Select(scanner.DescribeIssue).ToArray());
        }
    }

    public bool TryApplyIntegrityRepairs(long revision, IReadOnlyList<IndexIntegrityUpdate> updates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (revision != Interlocked.Read(ref _integrityRevision))
            return false;

        // Convert all documents before acquiring the writer exclusively.
        var operations = updates.Select(update => (Term: GetVersionIdTerm(update.VersionId),
            Document: update.Document == null ? null : GetDocument(update.Document))).ToArray();
        IndexReader committed;
        _catchUpLock.EnterWriteLock();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Running || _catchUp != null || _writer == null || revision != _integrityRevision)
                return false;
            if (operations.Length == 0)
                return true;

            var directory = _writer.GetDirectory();
            var metadata = IndexReader.GetCommitUserData(directory);
            cancellationToken.ThrowIfCancellationRequested();
            MarkIntegrityMutation();
            // Once writing starts, finish the batch before observing cancellation again.
            foreach (var (term, document) in operations)
            {
                if (document == null)
                    _writer.DeleteDocuments(term);
                else
                    _writer.UpdateDocument(term, document);
            }
            _writer.Commit(metadata);
            IndexReopenedAt = default;
            committed = IndexReader.Open(directory, true);
        }
        finally { _catchUpLock.ExitWriteLock(); }

        using (committed)
        {
            foreach (var (term, document) in operations)
            {
                var postings = committed.TermDocs(term);
                try
                {
                    if (document == null ? postings.Next() : !postings.Next() || postings.Next())
                        throw new InvalidDataException("The committed integrity repair did not match its expected document count.");
                }
                finally { postings.Close(); }
            }
        }
        return true;
    }

    private void RequireIntegrityWriter()
    {
        if (!Running || _catchUp != null || _writer == null)
            throw new InvalidOperationException("The local index is not available for integrity checking.");
    }

    private sealed class CommittedIntegrityReader(IndexReader reader, IndexSnapshot snapshot) : IIndexIntegrityReader
    {
        public long CommitVersion => reader.GetVersion();
        public IndexingActivityStatus Activity => ParseFromReader(reader);
        public int MaxDoc => reader.MaxDoc();
        public int ContentDocuments { get; private set; }

        public void Export(string scratchDirectory, Action<SavedIndexVersion> write, Action<Difference> issue,
            CancellationToken cancellationToken)
        {
            var issuePath = Path.Combine(scratchDirectory, "raw-issues.jsonl");
            using var output = File.CreateText(issuePath);
            var differenceReporter = new DifferenceReporter(row => output.WriteLine(JsonSerializer.Serialize(row)));
            using var scanner = new Integrity.LuceneSnapshot(reader, differenceReporter, cancellationToken,
                Path.Combine(scratchDirectory, "lucene.map"), identifyVersions: true);
            ContentDocuments = scanner.ContentDocuments;
            foreach (var row in scanner.ReadVersions())
                write(new(row.VersionId, row.DocId, scanner.GetMetadata(row.DocId)));
            output.Dispose();
            foreach (var line in File.ReadLines(issuePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                issue(scanner.DescribeIssue(JsonSerializer.Deserialize<Difference>(line)!));
            }
            File.Delete(issuePath);
        }

        public void Dispose()
        {
            try { reader.Dispose(); }
            finally { snapshot.Dispose(); }
        }
    }
}
