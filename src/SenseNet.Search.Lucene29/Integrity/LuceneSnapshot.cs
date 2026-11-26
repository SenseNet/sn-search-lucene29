#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SenseNet.Search.Indexing.Integrity;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Util;

namespace SenseNet.Search.Lucene29.Integrity;

/// <summary>Metadata of one fixed IndexReader snapshot; the caller owns the reader.</summary>
public sealed class LuceneSnapshot : IDisposable
{
    private readonly IndexReader _reader;
    private readonly DifferenceReporter _reporter;
    private readonly CancellationToken _cancel;
    private readonly DiskMetadata _metadata;
    private static readonly FieldSelector PathSelector = new MapFieldSelector(new[] { "Path" });
    private bool _enumerated;
    public int LiveDocuments { get; }
    public int ContentDocuments { get; private set; }
    public int MaxDoc => _metadata.Count;

    public LuceneSnapshot(IndexReader reader, DifferenceReporter reporter, CancellationToken cancel = default, string? scratchPath = null, bool identifyVersions = false)
    {
        _reader = reader;
        _reporter = reporter;
        _cancel = cancel;
        LiveDocuments = reader.NumDocs();
        _metadata = new DiskMetadata(reader.MaxDoc(), scratchPath);
        try { Initialize(identifyVersions); }
        catch { _metadata.Dispose(); throw; }
    }

    private void Initialize(bool identifyVersions)
    {
        ReadExact("$#COMMIT", "$#COMMIT", doc => { var data = _metadata[doc]; data.IsCommit = true; _metadata[doc] = data; });
        if (identifyVersions)
            ReadVersionIdentities();
        ReadNumeric("Id", false, Fields.NodeId, (doc, value) =>
        {
            var data = _metadata[doc]; data.NodeId = (int)value; _metadata[doc] = data;
            if (value <= 0) Invalid(doc, "Id", value);
        });
        ReadNumeric("NodeTimestamp", true, Fields.NodeTimestamp, (doc, value) => { var data = _metadata[doc]; data.NodeTimestamp = value; _metadata[doc] = data; });
        ReadNumeric("VersionTimestamp", true, Fields.VersionTimestamp, (doc, value) => { var data = _metadata[doc]; data.VersionTimestamp = value; _metadata[doc] = data; });
        ReadBoolean("IsLastPublic", Fields.IsLastPublic, (doc, value) => { var data = _metadata[doc]; data.IsLastPublic = value; _metadata[doc] = data; });
        ReadBoolean("IsLastDraft", Fields.IsLastDraft, (doc, value) => { var data = _metadata[doc]; data.IsLastDraft = value; _metadata[doc] = data; });

        for (var doc = 0; doc < MaxDoc; doc++)
        {
            _cancel.ThrowIfCancellationRequested();
            if (!IsContent(doc)) continue;
            ContentDocuments++;
            var metadata = _metadata[doc];
            CheckPresent(doc, metadata, Fields.NodeId, "Id");
            CheckPresent(doc, metadata, Fields.NodeTimestamp, "NodeTimestamp");
            CheckPresent(doc, metadata, Fields.VersionTimestamp, "VersionTimestamp");
            CheckPresent(doc, metadata, Fields.IsLastPublic, "IsLastPublic");
            CheckPresent(doc, metadata, Fields.IsLastDraft, "IsLastDraft");
        }
        PrepareHistoricalPaths();
    }

    public IndexMetadata GetMetadata(int docId)
    {
        var metadata = _metadata[docId];
        if (metadata.MissingDraftPath)
        {
            var draft = _metadata[metadata.DraftDocId];
            if (!draft.AmbiguousVersionId)
                metadata.DraftVersionId = draft.UniqueVersionId;
        }
        return metadata;
    }

    public Difference DescribeIssue(Difference issue)
    {
        if (issue.DocId is not int doc)
            return issue;
        var metadata = GetMetadata(doc);
        var versionId = !metadata.AmbiguousVersionId && metadata.UniqueVersionId > 0
            ? metadata.UniqueVersionId : (int?)null;
        return issue with
        {
            VersionId = versionId,
            NodeId = metadata.NodeId > 0 ? metadata.NodeId : null,
            Index = new SavedIndexVersion(versionId ?? 0, doc, metadata)
        };
    }

    public bool HasStoredPath(int docId) => _reader.Document(docId, PathSelector).Get("Path") != null;

    public bool HistoricalNodeTimestampIsAllowed(int docId)
    {
        var metadata = _metadata[docId];
        if (metadata.IsLastDraft) return false;
        if (metadata.MissingDraftPath)
        {
            var draft = _metadata[metadata.DraftDocId];
            if (!draft.DraftPathReported)
            {
                _reporter.Report(new(DifferenceKind.MissingField, DocId: metadata.DraftDocId, Field: "Path"));
                draft.DraftPathReported = true;
                _metadata[metadata.DraftDocId] = draft;
            }
        }
        if (metadata.MissingOwnPath) _reporter.Report(new(DifferenceKind.MissingField, DocId: docId, Field: "Path"));
        return metadata.HistoricalPathMatches;
    }

    private void PrepareHistoricalPaths()
    {
        var first = NumericUtils.IntToPrefixCoded(int.MinValue);
        var terms = _reader.Terms(new Term("Id", first));
        var docs = _reader.TermDocs();
        try
        {
            do
            {
                _cancel.ThrowIfCancellationRequested();
                var term = terms.Term();
                if (!IsFullPrecision(term, "Id", first[0])) break;
                int node;
                try { node = NumericUtils.PrefixCodedToInt(term.Text()); }
                catch (FormatException) { continue; }
                var draftId = -1;
                docs.Seek(terms);
                while (docs.Next())
                {
                    _cancel.ThrowIfCancellationRequested();
                    var doc = docs.Doc();
                    var data = _metadata[doc];
                    if (IsContent(doc) && data.NodeId == node && data.IsLastDraft) { draftId = doc; break; }
                }
                if (draftId < 0) continue;
                string? draftPath = null;
                var loaded = false;
                docs.Seek(terms);
                while (docs.Next())
                {
                    _cancel.ThrowIfCancellationRequested();
                    var doc = docs.Doc();
                    var data = _metadata[doc];
                    if (!IsContent(doc) || data.NodeId != node || data.IsLastDraft) continue;
                    if (!loaded) { draftPath = _reader.Document(draftId, PathSelector).Get("Path"); loaded = true; }
                    var path = _reader.Document(doc, PathSelector).Get("Path");
                    data.DraftDocId = draftId;
                    data.MissingOwnPath = path == null;
                    data.MissingDraftPath = draftPath == null;
                    data.HistoricalPathMatches = path != null && draftPath != null && string.Equals(path, draftPath, StringComparison.Ordinal);
                    _metadata[doc] = data;
                }
            } while (terms.Next());
        }
        finally { docs.Close(); terms.Close(); }
    }

    /// <summary>Full-precision numeric terms are ordered by VersionId; postings by DocId.</summary>
    public IEnumerable<IndexVersion> ReadVersions()
    {
        if (_enumerated) throw new InvalidOperationException("The version stream can only be enumerated once.");
        _enumerated = true;
        foreach (var (value, doc) in ReadVersionPostings())
        {
            MarkPresent(doc, Fields.VersionId, "VersionId");
            if (value is > 0)
                yield return new IndexVersion(value.Value, doc);
            else
                Invalid(doc, "VersionId", value);
        }

        // Documents without a VersionId never appear in the stream; do not silently omit them.
        for (var doc = 0; doc < MaxDoc; doc++)
        {
            _cancel.ThrowIfCancellationRequested();
            if (IsContent(doc)) CheckPresent(doc, _metadata[doc], Fields.VersionId, "VersionId");
        }
    }

    private void ReadVersionIdentities()
    {
        foreach (var (value, doc) in ReadVersionPostings())
        {
            var metadata = _metadata[doc];
            if (value is not > 0 || metadata.UniqueVersionId > 0 && metadata.UniqueVersionId != value)
                metadata.AmbiguousVersionId = true;
            else
                metadata.UniqueVersionId = value.Value;
            _metadata[doc] = metadata;
        }
    }

    private IEnumerable<(int? VersionId, int Document)> ReadVersionPostings()
    {
        var first = NumericUtils.IntToPrefixCoded(int.MinValue);
        var terms = _reader.Terms(new Term("VersionId", first));
        var docs = _reader.TermDocs();
        try
        {
            do
            {
                _cancel.ThrowIfCancellationRequested();
                var term = terms.Term();
                if (!IsFullPrecision(term, "VersionId", first[0]))
                    break;
                int? value = null;
                try { value = NumericUtils.PrefixCodedToInt(term.Text()); }
                catch (FormatException) { }
                docs.Seek(terms);
                while (docs.Next())
                {
                    _cancel.ThrowIfCancellationRequested();
                    var doc = docs.Doc();
                    if (IsContent(doc))
                        yield return (value, doc);
                }
            } while (terms.Next());
        }
        finally { docs.Close(); terms.Close(); }
    }

    private bool IsContent(int doc) => !_reader.IsDeleted(doc) && !_metadata[doc].IsCommit;
    private static bool IsFullPrecision(Term? term, string field, char prefix) => term != null &&
        term.Field() == field && term.Text().Length > 0 && term.Text()[0] == prefix;

    private void ReadNumeric(string field, bool isLong, Fields bit, Action<int, long> assign)
    {
        var first = isLong ? NumericUtils.LongToPrefixCoded(long.MinValue) : NumericUtils.IntToPrefixCoded(int.MinValue);
        var terms = _reader.Terms(new Term(field, first));
        var docs = _reader.TermDocs();
        var ids = new int[1024];
        var frequencies = new int[1024];
        try
        {
            do
            {
                _cancel.ThrowIfCancellationRequested();
                var term = terms.Term();
                if (!IsFullPrecision(term, field, first[0])) break;
                long? value = null;
                try { value = isLong ? NumericUtils.PrefixCodedToLong(term.Text()) : NumericUtils.PrefixCodedToInt(term.Text()); }
                catch (FormatException) { }
                docs.Seek(terms);
                int count;
                while ((count = docs.Read(ids, frequencies)) > 0)
                {
                    _cancel.ThrowIfCancellationRequested();
                    for (var i = 0; i < count; i++)
                    {
                        var doc = ids[i];
                        if (!IsContent(doc)) continue;
                        var firstValue = MarkPresent(doc, bit, field);
                        if (value is null) Invalid(doc, field, null);
                        else if (firstValue) assign(doc, value.Value);
                    }
                }
            } while (terms.Next());
        }
        finally { docs.Close(); terms.Close(); }
    }

    private void ReadBoolean(string field, Fields bit, Action<int, bool> assign)
    {
        var terms = _reader.Terms(new Term(field, ""));
        var docs = _reader.TermDocs();
        try
        {
            do
            {
                _cancel.ThrowIfCancellationRequested();
                var term = terms.Term();
                if (term == null || term.Field() != field) break;
                var text = term.Text();
                docs.Seek(terms);
                while (docs.Next())
                {
                    _cancel.ThrowIfCancellationRequested();
                    var doc = docs.Doc();
                    if (!IsContent(doc)) continue;
                    var firstValue = MarkPresent(doc, bit, field);
                    if (text != "yes" && text != "no") Invalid(doc, field, text);
                    else if (firstValue) assign(doc, text == "yes");
                }
            } while (terms.Next());
        }
        finally { docs.Close(); terms.Close(); }
    }

    private void ReadExact(string field, string value, Action<int> assign)
    {
        var docs = _reader.TermDocs(new Term(field, value));
        try
        {
            while (docs.Next()) { _cancel.ThrowIfCancellationRequested(); assign(docs.Doc()); }
        }
        finally { docs.Close(); }
    }

    private bool MarkPresent(int doc, Fields bit, string field)
    {
        if ((_metadata[doc].Present & bit) != 0)
        {
            _reporter.Report(new(DifferenceKind.MultipleFieldValues, DocId: doc, Field: field));
            return false;
        }
        var data = _metadata[doc]; data.Present |= bit; _metadata[doc] = data;
        return true;
    }
    private void CheckPresent(int doc, IndexMetadata metadata, Fields bit, string field)
    {
        if ((metadata.Present & bit) == 0)
            _reporter.Report(new(DifferenceKind.MissingField, DocId: doc, Field: field));
    }
    private void Invalid(int doc, string field, object? actual) =>
        _reporter.Report(new(DifferenceKind.InvalidField, DocId: doc, Field: field, Actual: actual));

    public void Dispose() => _metadata.Dispose();
}
