using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

// TODO(refactor): Experimental lifecycle acceptance tests; retain when the workflow API is finalized.
[TestClass]
public class RepairWorkflowTests
{
    // TODO(refactor): Experimental checker test adapter; consolidate existing IWorkflowOperations fixtures later.
    private sealed class Operations(RepairTests.Fixture fixture) : IWorkflowOperations
    {
        internal int DatabaseReads;
        internal int IndexReads;
        internal bool FailActivity;
        private readonly WorkflowOperations _real = new(TextWriter.Null);
        // TODO(refactor): Experimental fixture identity lookup; share checker fixtures later.
        public IndexIdentity InspectIndex(string path) => _real.InspectIndex(path);
        // TODO(refactor): Experimental live SQL identity substitute with real Lucene metadata; consolidate acceptance fixtures later.
        public Task<LastActivitySummary> ReadActivityAsync(string connection, string indexPath, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (FailActivity) throw new IOException("Activity read temporarily failed.");
            return Task.FromResult(new LastActivitySummary(fixture.Activity, WorkflowOperations.ReadIndexActivity(indexPath)));
        }
        // TODO(refactor): Experimental SQL export substitute; replace with opt-in real MSSQL acceptance fixtures later.
        public Task<DatabaseExport> ExportDatabaseAsync(string connection, string target, CancellationToken cancel)
        {
            DatabaseReads++;
            using var output = new CheckpointWriter(target);
            var rows = fixture.Rows.Values.Where(r => !r.Excluded).OrderBy(r => r.VersionId).ToArray();
            using (var writer = SnapshotFiles.StartDatabase(output.Stream))
                foreach (var r in rows) SnapshotFiles.Write(writer, new DatabaseVersion(r.VersionId, r.VersionTimestamp, r.NodeId, r.NodeTimestamp, r.LastPublic, r.LastDraft));
            return Task.FromResult(new DatabaseExport(output.Complete(rows.Length, cancel), 0, "Snapshot", fixture.Activity));
        }
        // TODO(refactor): Experimental real-index export adapter; share checker fixtures later.
        public Task<IndexExport> ExportIndexAsync(IndexIdentity identity, string target, string issues, string scratch, CancellationToken cancel)
        { IndexReads++; return _real.ExportIndexAsync(identity, target, issues, scratch, cancel); }
    }

    // TODO(refactor): Experimental persisted-state fixture; consolidate workflow assertions later.
    private static WorkflowState Load(string workspace)
    { using var state = new WorkflowWorkspace(workspace); return state.Load()!; }

    // TODO(refactor): Experimental repair factory fixture; replace with canonical session fixtures after validation.
    private static Func<string, IndexIdentity, CancellationToken, Task<ComparisonRepairer>> Factory(RepairTests.Fixture fixture, Action<string>? progress = null) =>
        (_, index, _) => Task.FromResult(fixture.Repair(progress, index));

    // TODO(refactor): Experimental prepared continuation test; retain without per-document resume semantics.
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparedWorkflowResumesWithoutNewExportsAndNeedsConfirmationAgain(bool missingActivitySnapshot)
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var operations = new Operations(fixture);
        var protector = new WorkflowTests.TestProtector();
        var prompts = 0;
        Task<bool> Confirm(IndexIdentity index, RepairResult result, CancellationToken cancel)
        {
            prompts++;
            Assert.ThrowsException<IOException>(() => new WorkflowWorkspace(workspace));
            return Task.FromResult(prompts > 1);
        }
        var runner = new WorkflowRunner(operations, protector, TextWriter.Null, repairFactory: Factory(fixture), confirmRepair: Confirm);
        var before = fixture.Identity;
        var prepared = await runner.RunAsync(new("Server=localhost;Database=example", fixture.IndexPath, workspace), default);
        Assert.AreEqual(WorkflowPhase.RepairPrepared, prepared.Phase);
        Assert.AreEqual(before, fixture.Identity);
        Assert.AreEqual(99, prepared.Database!.LastActivityId);
        Assert.AreEqual(2, prepared.IndexSnapshot!.Activity!.LastActivityId);
        CollectionAssert.AreEqual(new[] { 1 }, prepared.IndexSnapshot.Activity.GapPreview);
        var reportBytes = File.ReadAllBytes(Path.Combine(workspace, prepared.ReportFile));
        if (missingActivitySnapshot)
        {
            // Exercise completion against the active summary-enriched report name as well.
            File.Move(Path.Combine(workspace, prepared.ReportFile), Path.Combine(workspace, prepared.EnrichedReportFile));
            prepared.Comparison = prepared.Comparison! with
            { Report = prepared.Comparison.Report with { Name = prepared.EnrichedReportFile } };
            prepared.Database = prepared.Database with { LastActivityId = null };
            prepared.IndexSnapshot = prepared.IndexSnapshot with { Activity = null };
            using var saved = new WorkflowWorkspace(workspace);
            saved.Save(prepared);
        }
        var completed = await runner.RunAsync(new(null, null, workspace), default);
        Assert.AreEqual(WorkflowPhase.RepairCompleted, completed.Phase);
        Assert.AreEqual(99, completed.Database!.LastActivityId);
        Assert.AreEqual(2, completed.IndexSnapshot!.Activity!.LastActivityId);
        Assert.AreEqual(2, prompts);
        Assert.AreEqual(1, operations.DatabaseReads);
        Assert.AreEqual(1, operations.IndexReads);
        var reportPath = Path.Combine(workspace, completed.ReportFile);
        var completedBytes = File.ReadAllBytes(reportPath);
        CollectionAssert.AreEqual(reportBytes, completedBytes[..reportBytes.Length]);
        var lines = File.ReadAllLines(reportPath);
        using (var record = JsonDocument.Parse(lines[^1]))
        {
            Assert.AreEqual("repair_completed", record.RootElement.GetProperty("type").GetString());
            Assert.AreEqual(completed.Id, record.RootElement.GetProperty("workflowId").GetGuid());
            Assert.AreEqual(completed.Repair!.DeletedDocuments, record.RootElement.GetProperty("deletedDocuments").GetInt32());
            Assert.AreEqual(completed.Repair.WrittenVersions, record.RootElement.GetProperty("writtenVersions").GetInt32());
            Assert.AreEqual(99, record.RootElement.GetProperty("lastActivityId").GetInt32());
            Assert.AreEqual(0, record.RootElement.GetProperty("gaps").GetArrayLength());
        }
        Assert.AreEqual((long)lines.Length, completed.Comparison!.Report.Rows);
        Assert.AreEqual(completed.Comparison.DifferenceCount + 2, completed.Comparison.Report.Rows);
        await CheckpointWriter.VerifyAsync(workspace, completed.Comparison.Report, default);
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
        var after = fixture.Identity;
        protector.Unavailable = true;
        var repeated = await runner.RunAsync(new(null, null, workspace), default);
        Assert.AreEqual(WorkflowPhase.RepairCompleted, repeated.Phase);
        Assert.AreEqual(after, fixture.Identity);
        Assert.AreEqual(2, prompts);
        CollectionAssert.AreEqual(completedBytes, File.ReadAllBytes(reportPath));
        Assert.IsFalse(Directory.EnumerateFiles(workspace).Any(p => Path.GetFileName(p).StartsWith("repair-")));
        await using var replay = fixture.Repair();
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => replay.PrepareAsync(reportPath, default));
    }

    // TODO(refactor): Experimental interrupted lifecycle tests; preserve the no-resume mutation boundary.
    [DataTestMethod]
    [DataRow("RepairStarted")]
    [DataRow("deletions_committed")]
    [DataRow("version_written")]
    [DataRow("final_committed")]
    public async Task StartedRepairCannotResumeEvenAfterFinalIndexCommit(string stop)
    {
        using var fixture = new RepairTests.Fixture([CheckerTests.Doc(versionTs: 5)], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var operations = new Operations(fixture);
        var protector = new WorkflowTests.TestProtector();
        var runner = new WorkflowRunner(operations, protector, TextWriter.Null,
            checkpointSaved: phase => { if (phase.ToString() == stop) throw new OperationCanceledException(); },
            repairFactory: Factory(fixture, stage => { if (stage == stop) throw new OperationCanceledException(); }),
            confirmRepair: (_, _, _) => Task.FromResult(true));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => runner.RunAsync(new("Server=localhost", fixture.IndexPath, workspace), default));
        Assert.AreEqual(WorkflowPhase.RepairStarted, Load(workspace).Phase);
        Assert.IsFalse(File.ReadAllText(Path.Combine(workspace, Load(workspace).ReportFile)).Contains("repair_completed"));
        protector.Unavailable = true;
        using var diagnostics = new StringWriter();
        var exit = await Program.ExecuteAsync(new(null, null, workspace), operations, protector, TextWriter.Null, diagnostics, default, Factory(fixture));
        Assert.AreEqual(2, exit);
        StringAssert.Contains(diagnostics.ToString(), "new full check");
        Assert.IsFalse(diagnostics.ToString().Contains("Resume using -w"));
    }

    // TODO(refactor): Experimental durable-marker failure test; retain fail-before-write behavior.
    [TestMethod]
    public async Task FailedStartMarkerSaveCannotWriteTheIndex()
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var before = fixture.Identity;
        var runner = new WorkflowRunner(new Operations(fixture), new WorkflowTests.TestProtector(), TextWriter.Null,
            checkpointSaved: phase => { if (phase == WorkflowPhase.RepairPrepared) Directory.CreateDirectory(Path.Combine(workspace, "workflow.json.partial")); },
            repairFactory: Factory(fixture), confirmRepair: (_, _, _) => Task.FromResult(true));
        await Assert.ThrowsExceptionAsync<IOException>(() => runner.RunAsync(new("Server=localhost", fixture.IndexPath, workspace), default));
        Assert.AreEqual(before, fixture.Identity);
        Assert.AreEqual(WorkflowPhase.RepairPrepared, Load(workspace).Phase);
    }

    // TODO(refactor): Experimental preview integrity test; retain saved comparison checksum protection.
    [TestMethod]
    public async Task ModifiedPreparedReportIsRejectedBeforeAnotherPrompt()
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var runner = new WorkflowRunner(new Operations(fixture), new WorkflowTests.TestProtector(), TextWriter.Null,
            repairFactory: Factory(fixture), confirmRepair: (_, _, _) => Task.FromResult(false));
        var state = await runner.RunAsync(new("Server=localhost", fixture.IndexPath, workspace), default);
        File.AppendAllText(Path.Combine(workspace, state.ReportFile), "\n");
        var before = fixture.Identity;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => runner.RunAsync(new(null, null, workspace), default));
        Assert.AreEqual(before, fixture.Identity);
    }

    // TODO(refactor): Experimental console contract test; retain explicit opt-in when the UI is finalized.
    [DataTestMethod]
    [DataRow("igen", true, false)]
    [DataRow("Igen", true, false)]
    [DataRow("yes", true, true)]
    [DataRow("i", true, false)]
    [DataRow("y", true, true)]
    [DataRow("YES", true, true)]
    [DataRow(" I ", true, false)]
    [DataRow("Y", true, true)]
    [DataRow(" YeS ", true, true)]
    [DataRow(" y ", true, true)]
    [DataRow("", true, false)]
    [DataRow("  ", true, false)]
    [DataRow("nem", true, false)]
    [DataRow("no", true, false)]
    [DataRow("n", true, false)]
    [DataRow("1", true, false)]
    [DataRow("true", true, false)]
    [DataRow("yes please", true, false)]
    [DataRow("igen", false, false)]
    [DataRow("yes", false, false)]
    [DataRow("i", false, false)]
    [DataRow("y", false, false)]
    public async Task OnlyInteractiveExplicitConfirmationStartsRepair(string input, bool interactive, bool expected)
    {
        using var text = new StringReader(input);
        using var diagnostics = new StringWriter();
        Assert.AreEqual(expected, await Program.ConfirmRepairAsync(new(1, 1, 99), text, diagnostics, interactive, default));
        if (interactive) StringAssert.EndsWith(diagnostics.ToString(), "Perform the repair? [yes/NO] ");
    }

    // TODO(refactor): Experimental post-repair diagnostic test; retain current-commit reads without reapplying the saved report.
    [TestMethod]
    public async Task CompletedRepairSummaryReadsTheNewCommitAndEmptyGaps()
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var operations = new Operations(fixture);
        var protector = new WorkflowTests.TestProtector();
        using var first = new StringWriter();
        Assert.AreEqual(0, await Program.ExecuteAsync(new("Server=localhost", fixture.IndexPath, workspace),
            operations, protector, first, TextWriter.Null, default, Factory(fixture), (_, _, _) => Task.FromResult(true)));
        Assert.AreEqual("2 (1)", TemporaryModeTests.SummaryValues(first.ToString())["index"]);
        StringAssert.Contains(first.ToString(), "REPAIR SUMMARY:");
        Assert.IsFalse(first.ToString().Contains("\"type\":\"repair_completed\""));
        var firstRepair = first.ToString()[first.ToString().IndexOf("REPAIR SUMMARY:", StringComparison.Ordinal)..];
        var displayedRepair = TemporaryModeTests.SummaryValues(firstRepair.Replace("REPAIR SUMMARY:", "COMPARISON SUMMARY:"));
        Assert.AreEqual("completed", displayedRepair["status"]);
        Assert.AreEqual("1", displayedRepair["writtenVersions"]);
        Assert.AreEqual("0", displayedRepair["deletedDocuments"]);
        Assert.AreEqual("99", displayedRepair["database"]);
        Assert.AreEqual("99", displayedRepair["index"]);
        var after = fixture.Identity;
        Assert.AreNotEqual(Load(workspace).Index.CommitVersion, after.CommitVersion);
        using var resumed = new StringWriter();
        Assert.AreEqual(0, await Program.ExecuteAsync(new(null, null, workspace), operations, protector,
            resumed, TextWriter.Null, default, (_, _, _) => throw new AssertFailedException("Completed repair must not prepare again.")));
        var activity = TemporaryModeTests.SummaryValues(resumed.ToString());
        Assert.AreEqual("99", activity["database"]);
        Assert.AreEqual("2 (1)", activity["index"]);
        using var savedSummary = JsonDocument.Parse(File.ReadLines(Path.Combine(workspace, Load(workspace).ReportFile))
            .Single(line => JsonNode.Parse(line)!["type"]!.GetValue<string>() == "summary"));
        var savedActivity = savedSummary.RootElement.GetProperty("lastActivity");
        Assert.AreEqual(99, savedActivity.GetProperty("database").GetInt32());
        Assert.AreEqual(2, savedActivity.GetProperty("index").GetProperty("lastActivityId").GetInt32());
        Assert.AreEqual(1, savedActivity.GetProperty("index").GetProperty("gapCount").GetInt32());
        Assert.AreEqual(after, fixture.Identity);
        Assert.AreEqual(1, operations.DatabaseReads);
        Assert.AreEqual(1, operations.IndexReads);
    }

    // TODO(refactor): Experimental stdout/cleanup contract test; retain when the CLI API is stabilized.
    [TestMethod]
    public async Task TemporaryRepairCompletesBeforeCleanupAndReportsSuccess()
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        var exit = await Program.ExecuteAsync(new("Server=localhost", fixture.IndexPath, null), new Operations(fixture),
            new WorkflowTests.TestProtector(), output, diagnostics, default, Factory(fixture), (_, _, _) => Task.FromResult(true));
        Assert.AreEqual(0, exit);
        StringAssert.Contains(output.ToString(), "REPAIR SUMMARY:");
        Assert.IsFalse(output.ToString().Contains("\"type\":\"repair_completed\""));
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(new string('-', 47), lines[^1]);
        StringAssert.Contains(diagnostics.ToString(), "Temporary working directory removed.");
        Assert.AreEqual(0, (await fixture.CompareAsync(false)).Count);
    }

    // TODO(refactor): Experimental completion durability tests; retain the no-resume rule across report and state write failures.
    [DataTestMethod]
    [DataRow("report")]
    [DataRow("checkpoint")]
    [DataRow("changed-report")]
    public async Task FailedCompletionRecordingRequiresANewCheck(string failure)
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        FileStream? held = null;
        var runner = new WorkflowRunner(new Operations(fixture), new WorkflowTests.TestProtector(), TextWriter.Null,
            repairFactory: Factory(fixture, stage =>
            {
                if (stage != "final_committed") return;
                if (failure == "report")
                    held = new FileStream(Directory.GetFiles(workspace, "comparison-*.jsonl").Single(), FileMode.Open, FileAccess.Read, FileShare.Read);
                else if (failure == "changed-report")
                {
                    var report = Directory.GetFiles(workspace, "comparison-*.jsonl").Single();
                    File.WriteAllText(report, File.ReadAllText(report).Replace("differences", "DIFFERENCES"), new UTF8Encoding(false));
                }
                else Directory.CreateDirectory(Path.Combine(workspace, "workflow.json.partial"));
            }), confirmRepair: (_, _, _) => Task.FromResult(true));
        try
        {
            await Assert.ThrowsExceptionAsync<IOException>(() => runner.RunAsync(new("Server=localhost", fixture.IndexPath, workspace), default));
        }
        finally { held?.Dispose(); }
        var saved = Load(workspace);
        Assert.AreEqual(WorkflowPhase.RepairStarted, saved.Phase);
        Assert.AreEqual("99", fixture.Data()["LastActivityId"]);
        Assert.AreEqual(failure == "checkpoint", File.ReadAllText(Path.Combine(workspace, saved.ReportFile)).Contains("repair_completed"));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => runner.RunAsync(new(null, null, workspace), default));
    }

    // TODO(refactor): Experimental metadata-only result coverage; retain completion records for zero-document repairs.
    [TestMethod]
    public async Task MetadataOnlyRepairWritesCompletionRecordInTemporaryMode()
    {
        using var fixture = new RepairTests.Fixture([CheckerTests.Doc()], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var runner = new WorkflowRunner(new Operations(fixture), new WorkflowTests.TestProtector(), TextWriter.Null,
            repairFactory: Factory(fixture), confirmRepair: (_, _, _) => Task.FromResult(true));
        var state = await runner.RunAsync(new("Server=localhost", fixture.IndexPath, workspace), default, persistent: false);
        Assert.AreEqual(WorkflowPhase.RepairCompleted, state.Phase);
        Assert.IsFalse(File.Exists(Path.Combine(workspace, "workflow.json")));
        var lines = File.ReadAllLines(Path.Combine(workspace, state.ReportFile));
        Assert.AreEqual(2, lines.Length);
        using var record = JsonDocument.Parse(lines[^1]);
        Assert.AreEqual("repair_completed", record.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(0, record.RootElement.GetProperty("deletedDocuments").GetInt32());
        Assert.AreEqual(0, record.RootElement.GetProperty("writtenVersions").GetInt32());
        await CheckpointWriter.VerifyAsync(workspace, state.Comparison!.Report, default);
    }

    // TODO(refactor): Experimental saved-result preservation; retain successful completion when only live display fails.
    [TestMethod]
    public async Task DisplayFailureAfterRepairPreservesCompletionRecord()
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var operations = new Operations(fixture);
        var protector = new WorkflowTests.TestProtector();
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        Assert.AreEqual(2, await Program.ExecuteAsync(new("Server=localhost", fixture.IndexPath, workspace), operations,
            protector, output, diagnostics, default, Factory(fixture, stage =>
            { if (stage == "final_committed") operations.FailActivity = true; }), (_, _, _) => Task.FromResult(true)));
        var saved = Load(workspace);
        Assert.AreEqual(WorkflowPhase.RepairCompleted, saved.Phase);
        StringAssert.Contains(File.ReadAllText(Path.Combine(workspace, saved.ReportFile)), "repair_completed");
        StringAssert.Contains(diagnostics.ToString(), "Repair completed, but displaying its result failed.");
        Assert.IsFalse(diagnostics.ToString().Contains("Run a new full check"));
        operations.FailActivity = false;
        Assert.AreEqual(0, await Program.ExecuteAsync(new(null, null, workspace), operations, protector,
            output, diagnostics, default, (_, _, _) => throw new AssertFailedException("Repair must not run again.")));
    }

    // TODO(refactor): Experimental completed-report upgrade; retain the completion trailer when enriching comparison counters.
    [TestMethod]
    public async Task SummaryUpgradePreservesRepairCompletionRecord()
    {
        using var fixture = new RepairTests.Fixture([], [RepairTests.Row()]);
        var workspace = Path.Combine(fixture.Root, "workflow");
        var runner = new WorkflowRunner(new Operations(fixture), new WorkflowTests.TestProtector(), TextWriter.Null,
            repairFactory: Factory(fixture), confirmRepair: (_, _, _) => Task.FromResult(true));
        var state = await runner.RunAsync(new("Server=localhost", fixture.IndexPath, workspace), default);
        var path = Path.Combine(workspace, state.ReportFile);
        var lines = File.ReadAllLines(path);
        var originalRecord = lines[^1];
        var summary = JsonNode.Parse(lines[^2])!.AsObject();
        summary.Remove("differentDocuments");
        summary.Remove("lastActivity");
        lines[^2] = summary.ToJsonString(JsonOutput.Options);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        state.Comparison = state.Comparison! with { DifferentDocuments = null, LastActivity = null,
            Report = new(Path.GetFileName(path), new FileInfo(path).Length, lines.Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))) };
        using (var saved = new WorkflowWorkspace(workspace)) saved.Save(state);
        var resumed = await runner.RunAsync(new(null, null, workspace), default);
        Assert.AreEqual(1L, resumed.Comparison!.DifferentDocuments);
        Assert.AreEqual(3L, resumed.Comparison.Report.Rows);
        Assert.AreEqual(2, resumed.Comparison.LastActivity!.Index.LastActivityId);
        Assert.AreEqual(1, resumed.Comparison.LastActivity.Index.GapCount);
        Assert.AreEqual(originalRecord, File.ReadLines(Path.Combine(workspace, resumed.ReportFile)).Last());
        await CheckpointWriter.VerifyAsync(workspace, resumed.Comparison.Report, default);
    }
}
