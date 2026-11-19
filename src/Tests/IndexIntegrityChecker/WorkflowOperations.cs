using System.Text;
using System.Text.Json;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Microsoft.Data.SqlClient;

namespace IndexIntegrityChecker;

internal interface IWorkflowOperations
{
    IndexIdentity InspectIndex(string indexPath);
    Task<DatabaseExport> ExportDatabaseAsync(string connectionString, string target, CancellationToken cancel);
    Task<IndexExport> ExportIndexAsync(IndexIdentity identity, string target, string issuesTarget, string scratchPath, CancellationToken cancel);
    // TODO(refactor): Experimental read-only activity diagnostic; consolidate the existing workflow test seam later.
    Task<LastActivitySummary> ReadActivityAsync(string connectionString, string indexPath, CancellationToken cancel);
}

internal sealed class WorkflowOperations(TextWriter diagnostics) : IWorkflowOperations
{
    // TODO(refactor): Experimental current activity read; consolidate direct MSSQL/Lucene diagnostics after validation.
    public async Task<LastActivitySummary> ReadActivityAsync(string connectionString, string indexPath, CancellationToken cancel)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancel);
        var database = await MsSqlRepairSource.GetLastIndexingActivityIdAsync(connection, null, cancel);
        if (database < 0) throw new InvalidDataException("Invalid last database activity id.");
        cancel.ThrowIfCancellationRequested();
        return new(database, ReadIndexActivity(indexPath));
    }

    // TODO(refactor): Experimental current-commit read; share this read-only diagnostic with the supported Lucene API later.
    internal static IndexActivitySummary ReadIndexActivity(string indexPath)
    {
        if (!System.IO.Directory.Exists(indexPath)) throw new DirectoryNotFoundException("The source index directory is missing.");
        using var directory = FSDirectory.Open(new DirectoryInfo(indexPath));
        using var reader = IndexReader.Open(directory, true);
        return IndexActivitySummary.FromUserData(reader.GetCommitUserData());
    }

    public IndexIdentity InspectIndex(string indexPath)
    {
        var path = IndexPathResolver.Resolve(indexPath);
        using var directory = FSDirectory.Open(new DirectoryInfo(path));
        using var reader = IndexReader.Open(directory, true);
        return new(path, reader.GetVersion());
    }

    // TODO(refactor): Experimental activity capture alongside the SQL snapshot; consolidate offline export APIs later.
    public async Task<DatabaseExport> ExportDatabaseAsync(string connectionString, string target, CancellationToken cancel)
    {
        await using var source = await SqlVersionSource.OpenAsync(connectionString, cancel);
        var isolation = source.Isolation.ToString();
        diagnostics.WriteLine($"SQL isolation: {isolation}; excluded content types: {source.ExcludedTypeCount}.");
        using var output = new CheckpointWriter(target);
        long rows = 0;
        var previous = 0;
        using (var writer = SnapshotFiles.StartDatabase(output.Stream))
        {
            await foreach (var row in source.ReadVersionsAsync(cancel))
            {
                if (row.VersionId <= previous) throw new InvalidDataException("SQL versions must be unique, positive and ordered.");
                previous = row.VersionId;
                SnapshotFiles.Write(writer, row);
                if (++rows % 1_000_000 == 0) diagnostics.WriteLine($"Database export: {rows:N0} versions.");
            }
        }
        var lastActivity = await source.GetLastIndexingActivityIdAsync(cancel);
        await source.CompleteAsync(cancel);
        return new(output.Complete(rows, cancel), source.ExcludedTypeCount, isolation, lastActivity);
    }

    public Task<IndexExport> ExportIndexAsync(IndexIdentity identity, string target, string issuesTarget,
        string scratchPath, CancellationToken cancel)
    {
        using var directory = FSDirectory.Open(new DirectoryInfo(identity.Directory));
        using var reader = IndexReader.Open(directory, true);
        if (reader.GetVersion() != identity.CommitVersion)
            throw new InvalidDataException("The source index commit has changed since this workflow was initialized. Use matching snapshots in a new working directory.");
        var result = ExportReader(reader, target, issuesTarget, scratchPath, cancel, () =>
        {
            if (IndexReader.GetCurrentVersion(directory) != identity.CommitVersion)
                throw new InvalidDataException("The source index changed during export. The index checkpoint was not committed.");
        }, diagnostics);
        return Task.FromResult(result);
    }

    // TODO(refactor): Experimental activity capture from the same Lucene commit; consolidate snapshot APIs later.
    internal static IndexExport ExportReader(IndexReader reader, string target, string issuesTarget, string scratchPath,
        CancellationToken cancel, Action? validateSource = null, TextWriter? diagnostics = null)
    {
        using var output = new CheckpointWriter(target);
        using var issues = new CheckpointWriter(issuesTarget);
        long rows = 0;
        int maxDoc, contentDocuments;
        DifferenceReporter reporter;
        using (var issueWriter = new StreamWriter(issues.Stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        using (var writer = SnapshotFiles.StartIndex(output.Stream))
        {
            reporter = new DifferenceReporter(d => issueWriter.WriteLine(JsonSerializer.Serialize(d, JsonOutput.Options)));
            using var snapshot = new LuceneSnapshot(reader, reporter, cancel, scratchPath);
            maxDoc = snapshot.MaxDoc;
            contentDocuments = snapshot.ContentDocuments;
            foreach (var version in snapshot.ReadVersions())
            {
                SnapshotFiles.Write(writer, new SavedIndexVersion(version.VersionId, version.DocId, snapshot.GetMetadata(version.DocId)));
                if (++rows % 1_000_000 == 0) diagnostics?.WriteLine($"Index export: {rows:N0} documents.");
            }
        }
        validateSource?.Invoke();
        var savedData = output.Complete(rows, cancel);
        var savedIssues = issues.Complete(reporter.Count, cancel);
        return new(savedData, savedIssues, maxDoc, contentDocuments,
            IndexActivitySummary.FromUserData(reader.GetCommitUserData()));
    }
}
