using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IndexIntegrityChecker;

internal static class SavedComparison
{
    // TODO(refactor): Experimental repair completion record; consolidate report/state finalization after the repair API stabilizes.
    internal static async Task<ComparisonExport> AppendRepairResultAsync(WorkflowWorkspace workspace,
        WorkflowState state, CancellationToken cancel)
    {
        if (state.Phase != WorkflowPhase.RepairStarted || state.Comparison == null || state.Repair == null)
            throw new InvalidOperationException("Repair completion can only be recorded after a started repair.");
        var comparison = state.Comparison;
        var saved = comparison.Report;
        if (saved.Rows != comparison.DifferenceCount + 1)
            throw new InvalidDataException("The comparison report already has trailing records or invalid row metadata.");
        await using var file = new FileStream(workspace.FilePath(saved.Name), FileMode.Open, FileAccess.ReadWrite,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length != saved.Length) throw new InvalidDataException("The comparison report changed during repair.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var lastByte = -1;
        int read;
        while ((read = await file.ReadAsync(buffer, cancel)) != 0)
        {
            hash.AppendData(buffer, 0, read);
            lastByte = buffer[read - 1];
        }
        if (!string.Equals(Convert.ToHexString(hash.GetCurrentHash()), saved.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("The comparison report changed during repair.");
        var line = JsonSerializer.Serialize(new
        {
            type = "repair_completed", workflowId = state.Id, state.Repair.DeletedDocuments,
            state.Repair.WrittenVersions, state.Repair.LastActivityId, gaps = Array.Empty<int>()
        }, JsonOutput.Options);
        var bytes = Encoding.UTF8.GetBytes((lastByte is '\n' or '\r' ? "" : "\n") + line + "\n");
        await file.WriteAsync(bytes, cancel);
        await file.FlushAsync(cancel);
        file.Flush(flushToDisk: true);
        hash.AppendData(bytes);
        return comparison with { Report = new(saved.Name, file.Length, saved.Rows + 1,
            Convert.ToHexString(hash.GetHashAndReset())) };
    }

    // TODO(refactor): Experimental comparison activity persistence; consolidate snapshot and report metadata later.
    public static ComparisonExport Run(WorkflowWorkspace workspace, WorkflowState state, CancellationToken cancel,
        Action<string>? differenceOutput = null)
    {
        var activity = ReadSnapshotActivity(state);
        var stopwatch = Stopwatch.StartNew();
        using var output = new CheckpointWriter(workspace.FilePath(state.ReportFile));
        using var documents = new DifferentDocumentCounter(
            workspace.FilePath($"different-documents-{state.Id:N}.bits"), state.IndexSnapshot!.MaxDoc);
        CheckCounts counts;
        DifferenceReporter reporter;
        using (var writer = new StreamWriter(output.Stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        {
            reporter = new DifferenceReporter(d =>
            {
                documents.Observe(d);
                var line = JsonSerializer.Serialize(new
                {
                    type = "difference", d.Kind, d.VersionId, d.DocId, d.NodeId, d.Field, d.Expected, d.Actual
                }, JsonOutput.Options);
                writer.WriteLine(line);
                differenceOutput?.Invoke(line);
            });
            using (var issues = File.OpenText(workspace.FilePath(state.IssuesFile)))
            {
                long issueCount = 0;
                string? line;
                while ((line = issues.ReadLine()) != null)
                {
                    cancel.ThrowIfCancellationRequested();
                    reporter.Report(JsonSerializer.Deserialize<Difference>(line, JsonOutput.Options)
                        ?? throw new InvalidDataException("Invalid index issue record."));
                    issueCount++;
                }
                if (issueCount != state.IndexSnapshot!.Issues.Rows) throw new InvalidDataException("Unexpected number of index issue records.");
            }
            var database = SnapshotFiles.ReadDatabase(workspace.FilePath(state.DatabaseFile), state.Database!.Data.Rows, cancel);
            var index = SnapshotFiles.ReadIndex(workspace.FilePath(state.IndexFile), state.IndexSnapshot!.Data.Rows, state.IndexSnapshot.MaxDoc, cancel);
            using var reportedPaths = new DiskBitSet(workspace.FilePath($"comparison-scratch-{state.Id:N}.bits"), state.IndexSnapshot.MaxDoc);
            counts = Merge(database, index, reporter, reportedPaths, cancel);
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                type = "summary", status = reporter.Count == 0 ? "consistent" : "differences",
                counts.DatabaseVersions, counts.IndexDocuments, counts.MatchedDocuments,
                differences = reporter.Count, differentDocuments = documents.Count,
                countsByKind = reporter.Counts, elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                lastActivity = activity
            }, JsonOutput.Options));
        }
        return new(output.Complete(reporter.Count + 1, cancel), counts, reporter.Count, reporter.Counts,
            stopwatch.ElapsedMilliseconds, documents.Count, activity);
    }

    // TODO(refactor): Experimental saved-source activity contract; consolidate with the offline export API later.
    internal static LastActivitySummary ReadSnapshotActivity(WorkflowState state) =>
        state.Database?.LastActivityId is int database && state.IndexSnapshot?.Activity is { } index
            ? new(database, index)
            : throw new InvalidDataException("The saved snapshots have no complete activity state. Run a new full check in a new workspace.");

    private static CheckCounts Merge(IEnumerable<DatabaseVersion> database, IEnumerable<SavedIndexVersion> index,
        DifferenceReporter reporter, DiskBitSet reportedPaths, CancellationToken cancel)
    {
        using var db = database.GetEnumerator();
        using var ix = index.GetEnumerator();
        var hasDb = db.MoveNext(); var hasIx = ix.MoveNext();
        int lastDb = 0, lastIx = 0;
        long dbCount = 0, ixCount = 0, matched = 0;
        while (hasDb || hasIx)
        {
            cancel.ThrowIfCancellationRequested();
            if (hasDb && db.Current.VersionId <= lastDb) throw new InvalidDataException("Database snapshot is not ordered by unique positive VersionId.");
            if (hasIx && ix.Current.VersionId < lastIx) throw new InvalidDataException("Index snapshot is not ordered by VersionId.");
            if (hasDb && (!hasIx || db.Current.VersionId < ix.Current.VersionId))
            {
                reporter.Report(new(DifferenceKind.NotInIndex, db.Current.VersionId, NodeId: db.Current.NodeId));
                lastDb = db.Current.VersionId; dbCount++; hasDb = db.MoveNext(); continue;
            }
            var version = ix.Current.VersionId;
            var found = hasDb && db.Current.VersionId == version;
            long duplicates = 0;
            do
            {
                cancel.ThrowIfCancellationRequested();
                var current = ix.Current;
                if (++duplicates > 1) reporter.Report(new(DifferenceKind.MoreDocument, version, current.DocId, current.Metadata.NodeId));
                if (found) { Compare(db.Current, current, reporter, reportedPaths); matched++; }
                else reporter.Report(new(DifferenceKind.NotInDatabase, version, current.DocId, current.Metadata.NodeId));
                lastIx = version; ixCount++; hasIx = ix.MoveNext();
            } while (hasIx && ix.Current.VersionId == version);
            if (found) { lastDb = version; dbCount++; hasDb = db.MoveNext(); }
        }
        return new(dbCount, ixCount, matched);
    }

    private static void Compare(DatabaseVersion db, SavedIndexVersion row, DifferenceReporter reporter, DiskBitSet reportedPaths)
    {
        var ix = row.Metadata;
        void Difference(DifferenceKind kind, string field, object? expected, object? actual) =>
            reporter.Report(new(kind, row.VersionId, row.DocId, db.NodeId, field, expected, actual));
        if ((ix.Present & Fields.NodeId) != 0 && ix.NodeId != db.NodeId)
            Difference(DifferenceKind.DifferentNodeId, "Id", db.NodeId, ix.NodeId);
        if ((ix.Present & Fields.VersionTimestamp) != 0 && ix.VersionTimestamp != db.VersionTimestamp)
            Difference(DifferenceKind.DifferentVersionTimestamp, "VersionTimestamp", db.VersionTimestamp, ix.VersionTimestamp);
        if ((ix.Present & Fields.IsLastPublic) != 0 && ix.IsLastPublic != (db.VersionId == db.LastMajorVersionId))
            Difference(DifferenceKind.DifferentLastPublicFlag, "IsLastPublic", db.VersionId == db.LastMajorVersionId, ix.IsLastPublic);
        if ((ix.Present & Fields.IsLastDraft) != 0 && ix.IsLastDraft != (db.VersionId == db.LastMinorVersionId))
            Difference(DifferenceKind.DifferentLastDraftFlag, "IsLastDraft", db.VersionId == db.LastMinorVersionId, ix.IsLastDraft);
        if ((ix.Present & Fields.NodeTimestamp) != 0 && ix.NodeTimestamp != db.NodeTimestamp)
        {
            if (!ix.IsLastDraft)
            {
                if (ix.MissingDraftPath && reportedPaths.TryMark(ix.DraftDocId))
                    reporter.Report(new(DifferenceKind.MissingField, DocId: ix.DraftDocId, Field: "Path"));
                if (ix.MissingOwnPath) reporter.Report(new(DifferenceKind.MissingField, DocId: row.DocId, Field: "Path"));
            }
            if (ix.IsLastDraft || !ix.HistoricalPathMatches)
                Difference(DifferenceKind.DifferentNodeTimestamp, "NodeTimestamp", db.NodeTimestamp, ix.NodeTimestamp);
        }
    }
}

internal sealed class DiskBitSet : IDisposable
{
    private readonly FileStream _file;
    private readonly string _path;
    private readonly int _count;
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedViewAccessor? _view;
    public DiskBitSet(string path, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _path = path; _count = count;
        _file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _file.SetLength(((long)count + 7) / 8);
            if (count > 0)
            {
                _mapping = MemoryMappedFile.CreateFromFile(_file, null, 0, MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None, leaveOpen: true);
                try { _view = _mapping.CreateViewAccessor(); }
                catch { _mapping.Dispose(); throw; }
            }
        }
        catch { _file.Dispose(); File.Delete(_path); throw; }
    }
    public bool TryMark(int doc)
    {
        if ((uint)doc >= (uint)_count) throw new InvalidDataException("Invalid document reference.");
        var value = _view!.ReadByte(doc / 8);
        var mask = (byte)(1 << (doc % 8));
        if ((value & mask) != 0) return false;
        _view.Write(doc / 8, (byte)(value | mask));
        return true;
    }
    public void Dispose() { _view?.Dispose(); _mapping?.Dispose(); _file.Dispose(); File.Delete(_path); }
}
