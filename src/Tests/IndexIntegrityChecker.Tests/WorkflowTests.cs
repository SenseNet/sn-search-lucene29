using System.Text;
using System.Text.Json;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class WorkflowTests
{
    [DataTestMethod]
    [DataRow("Initialized")]
    [DataRow("DatabaseSaved")]
    [DataRow("IndexSaved")]
    [DataRow("ComparisonSaved")]
    [DataRow("Completed")]
    public async Task EveryCheckpointResumesWithWorkingDirectoryOnly(string checkpoint)
    {
        using var fixture = new Fixture();
        var phase = Enum.Parse<WorkflowPhase>(checkpoint);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => fixture.RunNew(saved =>
        { if (saved == phase) throw new OperationCanceledException(); }));
        var savedState = fixture.Load();
        Assert.AreEqual(phase, savedState.Phase);
        var databaseCalls = fixture.Operations.DatabaseCalls;
        var indexCalls = fixture.Operations.IndexCalls;
        if (phase >= WorkflowPhase.DatabaseSaved) fixture.Protector.Unavailable = true;
        if (phase >= WorkflowPhase.IndexSaved) fixture.Operations.Unavailable = true;
        var finished = await fixture.Resume();
        Assert.AreEqual(WorkflowPhase.Completed, finished.Phase);
        Assert.AreEqual(0L, finished.Comparison!.DifferenceCount);
        Assert.AreEqual(new CheckCounts(1, 1, 1), finished.Comparison.Counts);
        Assert.AreEqual(phase >= WorkflowPhase.DatabaseSaved ? databaseCalls : databaseCalls + 1, fixture.Operations.DatabaseCalls);
        Assert.AreEqual(phase >= WorkflowPhase.IndexSaved ? indexCalls : indexCalls + 1, fixture.Operations.IndexCalls);
        Assert.AreEqual(1, fixture.Operations.InspectCalls);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, finished.ReportFile)));
    }

    [TestMethod]
    public async Task FailedIndexExportPreservesDatabaseAndIgnoresPartialOutput()
    {
        using var fixture = new Fixture();
        fixture.Operations.FailIndex = true;
        await Assert.ThrowsExceptionAsync<IOException>(() => fixture.RunNew());
        var saved = fixture.Load();
        Assert.AreEqual(WorkflowPhase.DatabaseSaved, saved.Phase);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, saved.IndexFile + ".partial")));
        fixture.Operations.FailIndex = false;
        fixture.Protector.Unavailable = true;
        var result = await fixture.Resume();
        Assert.AreEqual(1, fixture.Operations.DatabaseCalls);
        Assert.AreEqual(0L, result.Comparison!.DifferenceCount);
    }

    [TestMethod]
    public async Task InterruptedDatabaseExportIsRestartedAndSecretsAreRedactedOnResume()
    {
        using var fixture = new Fixture();
        fixture.Operations.FailDatabase = true;
        var first = await Assert.ThrowsExceptionAsync<IOException>(() => fixture.RunNew());
        Assert.IsFalse(first.Message.Contains("test-secret", StringComparison.Ordinal));
        var state = fixture.Load();
        Assert.AreEqual(WorkflowPhase.Initialized, state.Phase);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, state.DatabaseFile + ".partial")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, state.DatabaseFile)));
        var resumed = await Assert.ThrowsExceptionAsync<IOException>(() => fixture.Resume());
        Assert.IsFalse(resumed.Message.Contains("test-secret", StringComparison.Ordinal));
        fixture.Operations.FailDatabase = false;
        Assert.AreEqual(WorkflowPhase.Completed, (await fixture.Resume()).Phase);
    }

    [TestMethod]
    public async Task InterruptedComparisonIsRebuiltFromSavedInputsWithoutSources()
    {
        using var fixture = new Fixture();
        await fixture.StopAt(WorkflowPhase.IndexSaved);
        var state = fixture.Load();
        File.WriteAllText(Path.Combine(fixture.Root, state.ReportFile + ".partial"), "truncated-json");
        fixture.Operations.Unavailable = true; fixture.Protector.Unavailable = true;
        var result = await fixture.Resume();
        var text = File.ReadAllText(Path.Combine(fixture.Root, result.ReportFile));
        Assert.IsFalse(text.Contains("truncated-json", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("\"type\":\"summary\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TamperedSavedDataIsRejectedEvenWhenItsLengthIsUnchanged()
    {
        using var fixture = new Fixture();
        await fixture.StopAt(WorkflowPhase.IndexSaved);
        var state = fixture.Load();
        var path = Path.Combine(fixture.Root, state.DatabaseFile);
        var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Resume());
        Assert.AreEqual(WorkflowPhase.IndexSaved, fixture.Load().Phase);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, state.ReportFile)));
    }

    [TestMethod]
    public async Task CompletedWorkflowDoesNotRecomputeOrRewriteItsReport()
    {
        using var fixture = new Fixture();
        var state = await fixture.RunNew();
        var path = Path.Combine(fixture.Root, state.ReportFile);
        var originalBytes = File.ReadAllBytes(path);
        var originalWriteTime = File.GetLastWriteTimeUtc(path);
        fixture.Operations.Unavailable = true; fixture.Protector.Unavailable = true;
        var result = await fixture.Resume();
        Assert.AreEqual(state.Id, result.Id);
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(path));
        Assert.AreEqual(originalWriteTime, File.GetLastWriteTimeUtc(path));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingOrTruncatedCheckpointIsRejected(bool truncate)
    {
        using var fixture = new Fixture();
        await fixture.StopAt(WorkflowPhase.IndexSaved);
        var state = fixture.Load();
        var path = Path.Combine(fixture.Root, state.IndexFile);
        if (truncate) File.WriteAllBytes(path, new byte[3]);
        else File.Delete(path);
        if (truncate) await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Resume());
        else await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => fixture.Resume());
        Assert.AreEqual(WorkflowPhase.IndexSaved, fixture.Load().Phase);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, state.ReportFile)));
    }

    [DataTestMethod]
    [DataRow("format")]
    [DataRow("identity")]
    [DataRow("checkpoint")]
    public async Task InvalidWorkflowStateIsRejected(string corruption)
    {
        using var fixture = new Fixture();
        await fixture.StopAt(WorkflowPhase.IndexSaved);
        var state = fixture.Load();
        if (corruption == "format") state.FormatVersion = 999;
        else if (corruption == "identity") state.Index = null!;
        else state.Database = new(null!, 0, "test");
        File.WriteAllText(Path.Combine(fixture.Root, "workflow.json"), JsonSerializer.Serialize(state, JsonOutput.Options));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Resume());
    }

    [TestMethod]
    public async Task EmptySnapshotsProduceACompletedConsistentReport()
    {
        using var fixture = new Fixture();
        fixture.Operations.Documents = Array.Empty<Document>();
        fixture.Operations.Rows = Array.Empty<DatabaseVersion>();
        var result = await fixture.RunNew();
        Assert.AreEqual(new CheckCounts(0, 0, 0), result.Comparison!.Counts);
        Assert.AreEqual(0L, result.Comparison.DifferenceCount);
        Assert.AreEqual(WorkflowPhase.Completed, (await fixture.Resume()).Phase);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    // TODO(refactor): Experimental compatibility coverage; retain saved comparison results without source access.
    public async Task CommandLineReturnsCompletedResultWithSavedActivity(bool differences)
    {
        using var fixture = new Fixture();
        if (differences) fixture.Operations.Documents = new[] { CheckerTests.Doc(versionTs: 201) };
        await fixture.StopAt(WorkflowPhase.Completed);
        Assert.IsTrue(CommandLineOptions.TryParse(new[] { "-w", fixture.Root }, out var options, out _));
        var exitCode = await Program.ExecuteAsync(options!, fixture.Operations, fixture.Protector,
            TextWriter.Null, TextWriter.Null, default);
        Assert.AreEqual(differences ? 1 : 0, exitCode);
        Assert.AreEqual(WorkflowPhase.Completed, fixture.Load().Phase);
        Assert.AreEqual(exitCode, await Program.ExecuteAsync(options!, fixture.Operations, fixture.Protector,
            TextWriter.Null, TextWriter.Null, default));
        Assert.AreEqual(0, fixture.Operations.ActivityReads);
    }

    [TestMethod]
    [TestCategory("WindowsProtection")]
    public async Task WindowsProtectedConnectionResumesAfterInitializationWithoutPlaintextSettings()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows DPAPI is required.");
        using var fixture = new Fixture();
        const string connection = "Server=localhost;Database=example;Password=workflow-test-secret";
        var protector = new WindowsConnectionProtector();
        var runner = new WorkflowRunner(fixture.Operations, protector, TextWriter.Null,
            phase => { if (phase == WorkflowPhase.Initialized) throw new OperationCanceledException(); });
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => runner.RunAsync(
            new(connection, Path.Combine(fixture.Root, "source-index"), fixture.Root), default));
        var state = fixture.Load();
        var encrypted = File.ReadAllBytes(Path.Combine(fixture.Root, state.CredentialsFile));
        Assert.IsFalse(Encoding.UTF8.GetString(encrypted).Contains("workflow-test-secret", StringComparison.Ordinal));
        Assert.IsFalse(File.ReadAllText(Path.Combine(fixture.Root, "workflow.json")).Contains("workflow-test-secret", StringComparison.Ordinal));
        StringAssert.Contains(protector.Unprotect(encrypted, state.Id), "workflow-test-secret");
        Assert.ThrowsException<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(encrypted, Guid.NewGuid()));
        var finished = await new WorkflowRunner(fixture.Operations, protector, TextWriter.Null)
            .RunAsync(new(null, null, fixture.Root), default);
        Assert.AreEqual(WorkflowPhase.Completed, finished.Phase);
    }

    [TestMethod]
    public async Task ConcurrentRunsAndSourceChangesAreRejected()
    {
        using var fixture = new Fixture();
        await fixture.StopAt(WorkflowPhase.Initialized);
        using (var locked = new WorkflowWorkspace(fixture.Root))
            await Assert.ThrowsExceptionAsync<IOException>(() => fixture.Resume());
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Runner().RunAsync(
            new(null, Path.Combine(fixture.Root, "different-index"), fixture.Root), default));
        Assert.AreEqual(WorkflowPhase.Initialized, fixture.Load().Phase);
    }

    [TestMethod]
    public async Task NewWorkflowRequiresBothSourcesButDoesNotChangeCurrentDirectory()
    {
        using var fixture = new Fixture();
        var cwd = Environment.CurrentDirectory;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Resume());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "workflow.json")));
        Assert.AreEqual(cwd, Environment.CurrentDirectory);
    }

    [TestMethod]
    public async Task SavedComparisonMatchesLiveCheckerForMalformedDuplicateAndHistoricalDocuments()
    {
        using var fixture = new Fixture();
        var missingVersion = CheckerTests.Doc(40, 4); missingVersion.RemoveField("VersionId");
        var malformed = CheckerTests.Doc(50, 5); malformed.RemoveField("IsLastDraft");
        var missingPath = CheckerTests.Doc(19, 2, 99, isPublic: false, isDraft: false, path: null);
        fixture.Operations.Documents = new[] {
            CheckerTests.Doc(9, nodeTs: 99, isPublic: false, isDraft: false), CheckerTests.Doc(),
            CheckerTests.Doc(versionTs: 201), missingPath, CheckerTests.Doc(20, 2, path: null),
            CheckerTests.Doc(30, 3), missingVersion, malformed };
        fixture.Operations.Rows = new[] { CheckerTests.Db(9), CheckerTests.Db(), CheckerTests.Db(19, 2, lastPublic:20,lastDraft:20),
            CheckerTests.Db(20,2,lastPublic:20,lastDraft:20), CheckerTests.Db(45,4), CheckerTests.Db(50,5,lastPublic:50,lastDraft:50) };
        var expected = await CheckerTests.Check(fixture.Operations.Documents, fixture.Operations.Rows);
        var result = await fixture.RunNew();
        Assert.AreEqual(expected.Counts, result.Comparison!.Counts);
        Assert.AreEqual((long)expected.Diffs.Count, result.Comparison.DifferenceCount);
        var actual = ReadDifferences(Path.Combine(fixture.Root, result.ReportFile));
        static string Normalize(Difference d) => JsonSerializer.Serialize(d, JsonOutput.Options);
        CollectionAssert.AreEquivalent(expected.Diffs.Select(Normalize).ToArray(), actual.Select(Normalize).ToArray());
    }

    [TestMethod]
    public void CancelledArtifactCommitLeavesPreviousCompletedFileUntouched()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "data.bin");
        File.WriteAllText(path, "completed");
        using (var writer = new CheckpointWriter(path))
        {
            writer.Stream.Write(Encoding.UTF8.GetBytes("partial replacement"));
            Assert.ThrowsException<OperationCanceledException>(() => writer.Complete(1, new CancellationToken(true)));
        }
        Assert.AreEqual("completed", File.ReadAllText(path));
        Assert.IsTrue(File.Exists(path + ".partial"));
    }

    [TestMethod]
    public void StreamingComparisonHandlesOneHundredThousandRecords()
    {
        using var fixture = new Fixture();
        using var workspace = new WorkflowWorkspace(fixture.Root);
        var state = new WorkflowState { OriginalIndexPath = fixture.Root, Index = new(fixture.Root, 1) };
        const int count = 100_000;
        using (var dbFile = new CheckpointWriter(workspace.FilePath(state.DatabaseFile)))
        {
            using (var writer = SnapshotFiles.StartDatabase(dbFile.Stream))
                for (var i = 1; i <= count; i++) SnapshotFiles.Write(writer, new DatabaseVersion(i, i, i, i, i, i));
            state.Database = new(dbFile.Complete(count, default), 0, "test", 0);
        }
        SavedFile ix;
        using (var ixFile = new CheckpointWriter(workspace.FilePath(state.IndexFile)))
        {
            using (var writer = SnapshotFiles.StartIndex(ixFile.Stream))
                for (var i = 1; i <= count; i++) SnapshotFiles.Write(writer, new SavedIndexVersion(i, i - 1, new IndexMetadata
                { NodeId = i, NodeTimestamp = i, VersionTimestamp = i, IsLastPublic = true, IsLastDraft = true, Present = Fields.Metadata | Fields.VersionId }));
            ix = ixFile.Complete(count, default);
        }
        using (var issues = new CheckpointWriter(workspace.FilePath(state.IssuesFile)))
            state.IndexSnapshot = new(ix, issues.Complete(0, default), count, count, new(0, [], 0));
        var result = SavedComparison.Run(workspace, state, default);
        Assert.AreEqual(new CheckCounts(count, count, count), result.Counts);
        Assert.AreEqual(0L, result.DifferenceCount);
        Assert.AreEqual(count + 1, File.ReadLines(workspace.FilePath(state.DatabaseFile)).Count());
        Assert.AreEqual(count + 1, File.ReadLines(workspace.FilePath(state.IndexFile)).Count());
        StringAssert.EndsWith(state.DatabaseFile, ".tsv");
        StringAssert.EndsWith(state.IndexFile, ".tsv");
    }

    [TestMethod]
    public void IndexCommitChangePreventsIndexCheckpoint()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "index");
        System.IO.Directory.CreateDirectory(path);
        using var directory = FSDirectory.Open(new DirectoryInfo(path));
        var writer = new IndexWriter(directory, new KeywordAnalyzer(), true, IndexWriter.MaxFieldLength.UNLIMITED);
        writer.AddDocument(CheckerTests.Doc()); writer.Close();
        var operations = new WorkflowOperations(TextWriter.Null);
        var identity = operations.InspectIndex(path);
        writer = new IndexWriter(directory, new KeywordAnalyzer(), false, IndexWriter.MaxFieldLength.UNLIMITED);
        writer.AddDocument(CheckerTests.Doc(20)); writer.Close();
        Assert.ThrowsExceptionAsync<InvalidDataException>(() => operations.ExportIndexAsync(identity,
            Path.Combine(fixture.Root, "ix.tsv"), Path.Combine(fixture.Root, "issues.jsonl"), Path.Combine(fixture.Root, "scratch.map"), default)).GetAwaiter().GetResult();
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "ix.tsv")));
    }

    internal static List<Difference> ReadDifferences(string path) => File.ReadLines(path)
        .Where(line =>
        {
            using var json = JsonDocument.Parse(line);
            return json.RootElement.GetProperty("type").GetString() == "difference";
        }).Select(line => JsonSerializer.Deserialize<Difference>(line, JsonOutput.Options)!).ToList();

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "IndexIntegrityChecker.Tests", Guid.NewGuid().ToString("N"));
        public TestOperations Operations { get; } = new();
        public TestProtector Protector { get; } = new();
        public Fixture() => System.IO.Directory.CreateDirectory(Root);
        public WorkflowRunner Runner(Action<WorkflowPhase>? saved = null) => new(Operations, Protector, TextWriter.Null, saved);
        public Task<WorkflowState> RunNew(Action<WorkflowPhase>? saved = null) => Runner(saved).RunAsync(
            new("Server=localhost;Database=example;Password=test-secret", Path.Combine(Root, "source-index"), Root), default);
        public Task<WorkflowState> Resume() => Runner().RunAsync(new(null, null, Root), default);
        public WorkflowState Load() { using var workspace = new WorkflowWorkspace(Root); return workspace.Load()!; }
        public Task StopAt(WorkflowPhase phase) => Assert.ThrowsExceptionAsync<OperationCanceledException>(() => RunNew(saved =>
            { if (saved == phase) throw new OperationCanceledException(); }));
        public void Dispose() => System.IO.Directory.Delete(Root, true);
    }

    internal sealed class TestProtector : IConnectionProtector
    {
        public bool Unavailable;
        // Deterministic test double; production uses WindowsConnectionProtector.
        public byte[] Protect(string value, Guid id) => Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
        public string Unprotect(byte[] value, Guid id)
        {
            if (Unavailable) throw new InvalidOperationException("Credentials are unavailable.");
            return Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(value)));
        }
    }

    internal sealed class TestOperations : IWorkflowOperations
    {
        internal int ActivityReads;
        internal bool ActivityUnavailable;
        internal LastActivitySummary Activity = new(1950, new(1897, [], 0));
        public int InspectCalls, DatabaseCalls, IndexCalls;
        public bool FailDatabase, FailIndex, Unavailable;
        public DatabaseVersion[] Rows = { CheckerTests.Db() };
        public Document[] Documents = { CheckerTests.Doc() };
        public IndexIdentity InspectIndex(string path) { InspectCalls++; return new(Path.GetFullPath(path), 1); }
        // TODO(refactor): Experimental live-diagnostic fixture; separate export and current activity availability in shared fixtures later.
        public Task<LastActivitySummary> ReadActivityAsync(string connection, string indexPath, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            ActivityReads++;
            if (ActivityUnavailable) throw new IOException("Activity read failed for " + connection);
            return Task.FromResult(Activity);
        }
        public Task<DatabaseExport> ExportDatabaseAsync(string connection, string target, CancellationToken cancel)
        {
            if (Unavailable) throw new IOException("Source unavailable.");
            DatabaseCalls++;
            using var output = new CheckpointWriter(target);
            using (var writer = SnapshotFiles.StartDatabase(output.Stream))
            {
                foreach (var row in Rows) SnapshotFiles.Write(writer, row);
                if (FailDatabase) throw new IOException("Failure with " + connection);
            }
            return Task.FromResult(new DatabaseExport(output.Complete(Rows.Length, cancel), 0, "test", Activity.Database));
        }
        public Task<IndexExport> ExportIndexAsync(IndexIdentity identity, string target, string issues, string scratch, CancellationToken cancel)
        {
            if (Unavailable) throw new IOException("Source unavailable.");
            IndexCalls++;
            if (FailIndex) { File.WriteAllText(target + ".partial", "incomplete"); throw new IOException("Interrupted index export."); }
            using var directory = new RAMDirectory();
            var writer = new IndexWriter(directory, new KeywordAnalyzer(), true, IndexWriter.MaxFieldLength.UNLIMITED);
            foreach (var doc in Documents) writer.AddDocument(doc);
            writer.Close();
            using var reader = IndexReader.Open(directory, true);
            return Task.FromResult(WorkflowOperations.ExportReader(reader, target, issues, scratch, cancel) with { Activity = Activity.Index });
        }
    }
}
