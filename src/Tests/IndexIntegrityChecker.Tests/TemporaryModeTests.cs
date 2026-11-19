using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class TemporaryModeTests
{
    private const string Connection = "Server=localhost;Database=example;Password=test-secret";

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TemporaryRunStreamsItsResultAndRemovesAllArtifacts(bool differences)
    {
        using var fixture = new WorkflowTests.Fixture();
        var sentinel = Path.Combine(fixture.Root, "keep.txt");
        File.WriteAllText(sentinel, "untouched");
        using var operations = new InspectingOperations(fixture.Operations);
        if (differences) fixture.Operations.Documents = new[] { CheckerTests.Doc(nodeTs: 101, versionTs: 201) };
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        var cwd = Environment.CurrentDirectory;
        var exitCode = await Program.ExecuteAsync(new(Connection, fixture.Root, null), operations,
            new UnavailableProtector(), output, diagnostics, default);
        Assert.AreEqual(differences ? 1 : 0, exitCode);
        Assert.AreEqual(cwd, Environment.CurrentDirectory);
        Assert.IsNotNull(operations.WorkingDirectory);
        Assert.IsFalse(Directory.Exists(operations.WorkingDirectory));
        Assert.AreEqual("untouched", File.ReadAllText(sentinel));
        StringAssert.Contains(diagnostics.ToString(), "Mode: temporary");
        Assert.IsFalse(diagnostics.ToString().Contains("Working directory:", StringComparison.Ordinal));
        StringAssert.Contains(diagnostics.ToString(), "Temporary working directory removed.");
        Assert.IsFalse(diagnostics.ToString().Contains("Report:", StringComparison.Ordinal));
        var lines = Lines(output);
        var summaryStart = Array.IndexOf(lines, "COMPARISON SUMMARY:");
        Assert.AreEqual(differences ? 2 : 0, summaryStart);
        foreach (var line in lines.Take(summaryStart))
        {
            using var difference = JsonDocument.Parse(line);
            Assert.AreEqual("difference", difference.RootElement.GetProperty("type").GetString());
        }
        var summary = SummaryValues(output.ToString());
        Assert.AreEqual(differences ? "2" : "0", summary["differences"]);
        Assert.AreEqual(differences ? "1" : "0", summary["differentDocuments"]);
        Assert.IsFalse(summary.ContainsKey("report"));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(19)]
    [DataRow(20)]
    [DataRow(21)]
    [DataRow(25)]
    public async Task DefaultLimitCapsOnlyOutputAndKeepsTheCompleteSummary(int differences)
    {
        using var fixture = new WorkflowTests.Fixture();
        fixture.Operations.Documents = Array.Empty<Lucene.Net.Documents.Document>();
        fixture.Operations.Rows = Enumerable.Range(1, differences).Select(v => CheckerTests.Db(v)).ToArray();
        using var operations = new InspectingOperations(fixture.Operations);
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        var exit = await Program.ExecuteAsync(new(Connection, fixture.Root, null), operations,
            new UnavailableProtector(), output, diagnostics, default);
        Assert.AreEqual(differences == 0 ? 0 : 1, exit);
        Assert.IsFalse(Directory.Exists(operations.WorkingDirectory));
        var lines = Lines(output);
        var summaryStart = Array.IndexOf(lines, "COMPARISON SUMMARY:");
        Assert.AreEqual(Math.Min(differences, 20), summaryStart);
        for (var i = 0; i < summaryStart; i++)
        {
            using var record = JsonDocument.Parse(lines[i]);
            Assert.AreEqual("difference", record.RootElement.GetProperty("type").GetString());
            Assert.AreEqual(i + 1, record.RootElement.GetProperty("versionId").GetInt32());
        }
        var result = SummaryValues(output.ToString());
        Assert.AreEqual(differences == 0 ? "consistent" : "differences", result["status"]);
        Assert.AreEqual(differences.ToString(), result["differences"]);
        Assert.AreEqual(differences.ToString(), result["differentDocuments"]);
        Assert.AreEqual(differences.ToString(), result["databaseVersions"]);
        Assert.AreEqual("0", result["indexDocuments"]);
        Assert.AreEqual("0", result["matchedDocuments"]);
        Assert.IsFalse(result.ContainsKey("report"));
        if (differences > 0)
            Assert.AreEqual(differences.ToString(), result["NotInIndex"]);
        var notices = Lines(diagnostics).Where(line => line.StartsWith("Only the first ", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(differences > 20 ? 1 : 0, notices.Length);
        if (notices.Length > 0)
        {
            StringAssert.Contains(notices[0], "first 20 differences");
            StringAssert.Contains(notices[0], "Rerun with -w");
        }
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public async Task CustomLimitSharesOneBudgetAcrossIssuesAndComparisonEvenWhenRedirected(int limit)
    {
        using var fixture = new WorkflowTests.Fixture();
        fixture.Operations.Rows = Enumerable.Range(1, 25)
            .Select(v => CheckerTests.Db(v, v, lastPublic: v, lastDraft: v)).ToArray();
        fixture.Operations.Documents = Enumerable.Range(1, 25)
            .Select(v => CheckerTests.Doc(v, v, nodeTs: 101, versionTs: 201)).ToArray();
        fixture.Operations.Documents[0].RemoveField("NodeTimestamp");
        fixture.Operations.Documents[0].RemoveField("IsLastPublic");
        var full = await fixture.RunNew();
        var reportLines = File.ReadAllLines(Path.Combine(fixture.Root, full.ReportFile));
        Assert.AreEqual(51L, full.Comparison!.DifferenceCount);
        Assert.AreEqual(25L, full.Comparison.DifferentDocuments);

        Assert.IsTrue(CommandLineOptions.TryParse(
            new[] { "-d", Connection, "-i", fixture.Root, "--max-console-differences=" + limit },
            out var options, out var error), error);
        using var diagnostics = new StringWriter();
        using var operations = new InspectingOperations(fixture.Operations);
        var outputPath = Path.Combine(fixture.Root, "stdout.txt");
        using (var output = new StreamWriter(outputPath))
            Assert.AreEqual(1, await Program.ExecuteAsync(options!, operations,
                new UnavailableProtector(), output, diagnostics, default));
        Assert.IsFalse(Directory.Exists(operations.WorkingDirectory));
        var lines = File.ReadAllLines(outputPath);
        Assert.AreEqual(limit, Array.IndexOf(lines, "COMPARISON SUMMARY:"));
        CollectionAssert.AreEqual(reportLines.Take(limit).ToArray(), lines.Take(limit).ToArray());
        foreach (var line in lines.Take(limit))
        {
            using var record = JsonDocument.Parse(line);
            Assert.AreEqual("difference", record.RootElement.GetProperty("type").GetString());
        }
        var summary = SummaryValues(File.ReadAllText(outputPath));
        using var fullSummary = JsonDocument.Parse(reportLines[^1]);
        foreach (var name in new[] { "differences", "differentDocuments",
                     "databaseVersions", "indexDocuments", "matchedDocuments" })
            Assert.AreEqual(fullSummary.RootElement.GetProperty(name).GetRawText(),
                summary[name], name);
        foreach (var kind in fullSummary.RootElement.GetProperty("countsByKind").EnumerateObject())
            Assert.AreEqual(kind.Value.GetRawText(), summary[kind.Name], kind.Name);
        var notices = Lines(diagnostics).Where(line => line.StartsWith("Only the first ", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(1, notices.Length);
        StringAssert.Contains(notices[0], $"first {limit} differences");
        StringAssert.Contains(notices[0], "Rerun with -w");
    }

    [DataTestMethod]
    [DataRow(false, 2)]
    [DataRow(true, 130)]
    public async Task FailureOrCancellationAfterOutputLimitStillPreventsSummaryAndCleansUp(bool cancel, int expectedExit)
    {
        using var fixture = new WorkflowTests.Fixture();
        fixture.Operations.Documents = Array.Empty<Lucene.Net.Documents.Document>();
        fixture.Operations.Rows = Enumerable.Range(1, 25).Select(v => CheckerTests.Db(v))
            .Append(CheckerTests.Db(1)).ToArray();
        using var operations = new InspectingOperations(fixture.Operations);
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        using var diagnostics = new NoticeWriter(() => { if (cancel) cancellation.Cancel(); });
        Assert.AreEqual(expectedExit, await Program.ExecuteAsync(new(Connection, fixture.Root, null),
            operations, new UnavailableProtector(), output, diagnostics, cancellation.Token));
        Assert.IsTrue(diagnostics.NoticeSeen);
        Assert.AreEqual(20, Lines(output).Length);
        foreach (var line in Lines(output))
        {
            using var record = JsonDocument.Parse(line);
            Assert.AreEqual("difference", record.RootElement.GetProperty("type").GetString());
        }
        Assert.IsFalse(Directory.Exists(operations.WorkingDirectory));
        StringAssert.Contains(diagnostics.ToString(), cancel ? "Cancelled." : "Check failed:");
        StringAssert.Contains(diagnostics.ToString(), "Temporary working directory removed.");
    }

    [DataTestMethod]
    [DataRow("database", 2)]
    [DataRow("index", 2)]
    [DataRow("cancel", 130)]
    [DataRow("comparison", 2)]
    [DataRow("output", 2)]
    public async Task FailureOrCancellationCleansTheTemporaryDirectory(string failure, int expectedExit)
    {
        using var fixture = new WorkflowTests.Fixture();
        using var cancellation = new CancellationTokenSource();
        using var operations = new InspectingOperations(fixture.Operations)
        { AfterIndex = failure == "cancel" ? cancellation.Cancel : null };
        fixture.Operations.FailDatabase = failure == "database";
        fixture.Operations.FailIndex = failure == "index";
        if (failure == "comparison") fixture.Operations.Rows = new[] { CheckerTests.Db(20), CheckerTests.Db() };
        if (failure == "output") fixture.Operations.Documents = new[] { CheckerTests.Doc(versionTs: 201) };
        using var captured = new StringWriter();
        TextWriter output = failure == "output" ? new BrokenOutput() : captured;
        using var diagnostics = new StringWriter();
        Assert.AreEqual(expectedExit, await Program.ExecuteAsync(new(Connection, fixture.Root, null), operations,
            new UnavailableProtector(), output, diagnostics, cancellation.Token));
        Assert.IsNotNull(operations.WorkingDirectory);
        Assert.IsFalse(Directory.Exists(operations.WorkingDirectory));
        Assert.IsFalse(diagnostics.ToString().Contains("test-secret", StringComparison.Ordinal));
        Assert.IsFalse(diagnostics.ToString().Contains("Resume using -w", StringComparison.Ordinal));
        Assert.IsFalse(captured.ToString().Contains("COMPARISON SUMMARY:", StringComparison.Ordinal));
        StringAssert.Contains(diagnostics.ToString(), "Temporary working directory removed.");
    }

    [TestMethod]
    public async Task EarlyCancellationReportsCleanupBeforeInspectingSources()
    {
        using var fixture = new WorkflowTests.Fixture();
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        Assert.AreEqual(130, await Program.ExecuteAsync(new(Connection, fixture.Root, null), fixture.Operations,
            new UnavailableProtector(), output, diagnostics, new CancellationToken(true)));
        StringAssert.Contains(diagnostics.ToString(), "Temporary working directory removed.");
        Assert.AreEqual(0, fixture.Operations.InspectCalls);
    }

    [TestMethod]
    public async Task PersistentModeRetainsItsReportAndResumesWithoutNewExports()
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "saved");
        fixture.Operations.Rows = Enumerable.Range(1, 25)
            .Select(v => CheckerTests.Db(v, v, lastPublic: v, lastDraft: v)).ToArray();
        fixture.Operations.Documents = Enumerable.Range(1, 25)
            .Select(v => CheckerTests.Doc(v, v, nodeTs: 101, versionTs: 201)).ToArray();
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        Assert.AreEqual(1, await Program.ExecuteAsync(new(Connection, fixture.Root, path), fixture.Operations,
            fixture.Protector, output, diagnostics, default));
        Assert.AreEqual("COMPARISON SUMMARY:", Lines(output)[0]);
        var summary = SummaryValues(output.ToString());
        string report;
        using (var savedWorkspace = new WorkflowWorkspace(path))
            report = Path.Combine(path, savedWorkspace.Load()!.ReportFile);
        Assert.AreEqual("50", summary["differences"]);
        Assert.AreEqual(51, File.ReadAllLines(report).Length);
        Assert.IsFalse(diagnostics.ToString().Contains("Only the first ", StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(report));
        Assert.IsTrue(File.Exists(Path.Combine(path, "workflow.json")));
        Assert.AreEqual(1, Directory.GetFiles(path, "*.dpapi").Length);
        StringAssert.Contains(diagnostics.ToString(), "Mode: persistent");
        Assert.IsFalse(diagnostics.ToString().Contains("Working directory:", StringComparison.Ordinal));
        var bytes = File.ReadAllBytes(report);
        fixture.Operations.Unavailable = true;
        using var resumed = new StringWriter();
        Assert.AreEqual(1, await Program.ExecuteAsync(new(null, null, path), fixture.Operations,
            fixture.Protector, resumed, diagnostics, default));
        Assert.AreEqual(output.ToString(), resumed.ToString());
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(report));
    }

    [TestMethod]
    public void TemporaryDirectoryCleanupOnlyRemovesTheOwnedRun()
    {
        using var first = new TemporaryWorkingDirectory();
        using var second = new TemporaryWorkingDirectory();
        Assert.AreNotEqual(first.DirectoryPath, second.DirectoryPath);
        var nested = Directory.CreateDirectory(Path.Combine(first.DirectoryPath, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "scratch"), "temporary");
        var sibling = Path.Combine(second.DirectoryPath, "keep");
        File.WriteAllText(sibling, "untouched");
        first.Dispose();
        Assert.IsFalse(Directory.Exists(first.DirectoryPath));
        Assert.AreEqual("untouched", File.ReadAllText(sibling));
    }

    [TestMethod]
    public async Task FailedCleanupReportsTheRemainingDirectoryAndReturnsAnError()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This test requires Windows file-sharing semantics.");
        using var fixture = new WorkflowTests.Fixture();
        using var operations = new InspectingOperations(fixture.Operations) { HoldIndexFile = true };
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        Assert.AreEqual(2, await Program.ExecuteAsync(new(Connection, fixture.Root, null), operations,
            new UnavailableProtector(), output, diagnostics, default));
        Assert.IsTrue(Directory.Exists(operations.WorkingDirectory));
        StringAssert.Contains(diagnostics.ToString(), "Could not remove temporary working directory");
        StringAssert.Contains(diagnostics.ToString(), operations.WorkingDirectory!);
    }

    // TODO(refactor): Experimental activity-summary persistence coverage; retain boundary cases in the offline reporting API.
    [DataTestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(5, true)]
    [DataRow(6, true)]
    [DataRow(18, false)]
    public async Task SummaryRetainsActivityAndBoundsGapPreview(int gapCount, bool differences)
    {
        using var fixture = new WorkflowTests.Fixture();
        if (differences) fixture.Operations.Documents = [CheckerTests.Doc(nodeTs: 101, versionTs: 201)];
        var gaps = Enumerable.Range(1800, gapCount).ToArray();
        var data = new Dictionary<string, string> { ["LastActivityId"] = "1897" };
        if (gapCount > 0) data["Gaps"] = string.Join(',', gaps);
        fixture.Operations.Activity = new(1950, IndexActivitySummary.FromUserData(data));
        var state = await fixture.RunNew();
        fixture.Operations.Unavailable = true;
        fixture.Protector.Unavailable = true;
        state = await fixture.Resume();
        Assert.AreEqual(gapCount, state.IndexSnapshot!.Activity!.GapCount);
        CollectionAssert.AreEqual(gaps.Take(5).ToArray(), state.IndexSnapshot.Activity.GapPreview);
        using var output = new StringWriter();
        Program.WriteComparisonSummary(output, state);
        using var summary = JsonDocument.Parse(File.ReadLines(Path.Combine(fixture.Root, state.ReportFile)).Last());
        var savedActivity = summary.RootElement.GetProperty("lastActivity");
        Assert.AreEqual(1950, savedActivity.GetProperty("database").GetInt32());
        Assert.AreEqual(1897, savedActivity.GetProperty("index").GetProperty("lastActivityId").GetInt32());
        Assert.AreEqual(gapCount, savedActivity.GetProperty("index").GetProperty("gapCount").GetInt32());
        CollectionAssert.AreEqual(gaps.Take(5).ToArray(), savedActivity.GetProperty("index").GetProperty("gapPreview")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray());
        var text = output.ToString();
        var values = SummaryValues(text);
        Assert.AreEqual("1950", values["database"]);
        var expectedGaps = gapCount == 0 ? "" : " (" + string.Join(", ", gaps.Take(5))
            + (gapCount > 5 ? $", ... {gapCount} total" : "") + ")";
        Assert.AreEqual("1897" + expectedGaps, values["index"]);
        Assert.AreEqual(differences, text.Contains("  COUNTS BY KIND:"));
        StringAssert.Contains(text, "  LAST ACTIVITY:");
        Assert.AreEqual(new string('-', 47), Lines(output)[^1]);
    }

    // TODO(refactor): Experimental saved-state reporting; retain comparison activity despite changed or unavailable sources.
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompletedSummaryAlwaysUsesSavedComparisonActivity(bool missingActivity)
    {
        using var fixture = new WorkflowTests.Fixture();
        fixture.Operations.Activity = new(2100, new(2050, [2001, 2002], 2));
        var state = await fixture.RunNew();
        state.Database = state.Database! with { LastActivityId = missingActivity ? null : 1 };
        state.IndexSnapshot = state.IndexSnapshot! with { Activity = missingActivity ? null : new(1, [1], 1) };
        using (var workspace = new WorkflowWorkspace(fixture.Root)) workspace.Save(state);
        fixture.Operations.Unavailable = true;
        fixture.Operations.ActivityUnavailable = true;
        fixture.Protector.Unavailable = true;
        var reportPath = Path.Combine(fixture.Root, state.ReportFile);
        var report = File.ReadAllBytes(reportPath);
        using var output = new StringWriter();
        Assert.AreEqual(0, await Program.ExecuteAsync(new(null, null, fixture.Root), fixture.Operations,
            fixture.Protector, output, TextWriter.Null, default));
        var values = SummaryValues(output.ToString());
        Assert.AreEqual("2100", values["database"]);
        Assert.AreEqual("2050 (2001, 2002)", values["index"]);
        Assert.AreEqual(new string('-', 47), Lines(output)[^1]);
        fixture.Operations.Activity = new(2200, new(2200, [], 0));
        using var next = new StringWriter();
        Assert.AreEqual(0, await Program.ExecuteAsync(new(null, null, fixture.Root), fixture.Operations,
            fixture.Protector, next, TextWriter.Null, default));
        var refreshed = SummaryValues(next.ToString());
        Assert.AreEqual("2100", refreshed["database"]);
        Assert.AreEqual("2050 (2001, 2002)", refreshed["index"]);
        Assert.AreEqual(0, fixture.Operations.ActivityReads);
        Assert.AreEqual(1, fixture.Operations.DatabaseCalls);
        Assert.AreEqual(1, fixture.Operations.IndexCalls);
        CollectionAssert.AreEqual(report, File.ReadAllBytes(reportPath));
    }

    // TODO(refactor): Experimental legacy summary enrichment; retain saved-source recovery without substituting live activity.
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LegacySummaryUsesSavedActivityOrRequiresNewCheck(bool missingSnapshots)
    {
        using var fixture = new WorkflowTests.Fixture();
        var state = await fixture.RunNew();
        var path = Path.Combine(fixture.Root, state.ReportFile);
        var lines = File.ReadAllLines(path);
        var summary = JsonNode.Parse(lines[^1])!.AsObject();
        summary.Remove("lastActivity");
        lines[^1] = summary.ToJsonString(JsonOutput.Options);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        var original = File.ReadAllBytes(path);
        state.Comparison = state.Comparison! with { LastActivity = null,
            Report = new(Path.GetFileName(path), original.Length, lines.Length, Convert.ToHexString(SHA256.HashData(original))) };
        if (missingSnapshots) state.Database = state.Database! with { LastActivityId = null };
        using (var workspace = new WorkflowWorkspace(fixture.Root)) workspace.Save(state);
        fixture.Operations.ActivityUnavailable = true;
        fixture.Operations.Unavailable = true;
        fixture.Protector.Unavailable = true;
        using var output = new StringWriter();
        using var diagnostics = new StringWriter();
        Assert.AreEqual(missingSnapshots ? 2 : 0, await Program.ExecuteAsync(new(null, null, fixture.Root), fixture.Operations,
            fixture.Protector, output, diagnostics, default));
        Assert.AreEqual(state.Phase, fixture.Load().Phase);
        Assert.AreEqual(0, fixture.Operations.ActivityReads);
        if (missingSnapshots)
        {
            Assert.AreEqual("", output.ToString());
            StringAssert.Contains(diagnostics.ToString(), "Run a new full check");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsNull(fixture.Load().Comparison!.LastActivity);
        }
        else
        {
            Assert.AreEqual("1950", SummaryValues(output.ToString())["database"]);
            Assert.AreEqual("1897", SummaryValues(output.ToString())["index"]);
            state = fixture.Load();
            using var saved = JsonDocument.Parse(File.ReadLines(Path.Combine(fixture.Root, state.ReportFile)).Last());
            Assert.AreEqual(1950, saved.RootElement.GetProperty("lastActivity").GetProperty("database").GetInt32());
            Assert.AreEqual(1897, state.Comparison!.LastActivity!.Index.LastActivityId);
            await CheckpointWriter.VerifyAsync(fixture.Root, state.Comparison.Report, default);
        }
    }

    // TODO(refactor): Experimental readable-summary assertion helper; consolidate console fixtures later.
    internal static Dictionary<string, string> SummaryValues(string text) => text
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .SkipWhile(line => line != "COMPARISON SUMMARY:").Skip(1)
        .TakeWhile(line => !line.StartsWith("---", StringComparison.Ordinal))
        .Where(line => !line.TrimEnd().EndsWith(':'))
        .Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    private sealed class UnavailableProtector : IConnectionProtector
    {
        public byte[] Protect(string value, Guid id) => throw new InvalidOperationException("Temporary runs must not persist credentials.");
        public string Unprotect(byte[] value, Guid id) => throw new InvalidOperationException("Temporary runs must not read persisted credentials.");
    }

    private sealed class NoticeWriter(Action onNotice) : StringWriter
    {
        public bool NoticeSeen { get; private set; }
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value?.StartsWith("Only the first ", StringComparison.Ordinal) == true)
            {
                NoticeSeen = true;
                onNotice();
            }
        }
    }

    private sealed class BrokenOutput : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException("Output is unavailable.");
    }

    private sealed class InspectingOperations(WorkflowTests.TestOperations inner) : IWorkflowOperations, IDisposable
    {
        private FileStream? _held;
        public string? WorkingDirectory { get; private set; }
        public Action? AfterIndex { get; init; }
        public bool HoldIndexFile { get; init; }
        public IndexIdentity InspectIndex(string path) => inner.InspectIndex(path);
        // TODO(refactor): Experimental forwarding of current activity diagnostics; consolidate temporary workflow fixtures later.
        public Task<LastActivitySummary> ReadActivityAsync(string connection, string indexPath, CancellationToken cancel) =>
            inner.ReadActivityAsync(connection, indexPath, cancel);

        public Task<DatabaseExport> ExportDatabaseAsync(string connection, string target, CancellationToken cancel)
        {
            InspectDirectory(Path.GetDirectoryName(target)!);
            return inner.ExportDatabaseAsync(connection, target, cancel);
        }
        public async Task<IndexExport> ExportIndexAsync(IndexIdentity identity, string target, string issues, string scratch, CancellationToken cancel)
        {
            InspectDirectory(Path.GetDirectoryName(target)!);
            var result = await inner.ExportIndexAsync(identity, target, issues, scratch, cancel);
            if (HoldIndexFile) _held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            AfterIndex?.Invoke();
            return result;
        }
        private void InspectDirectory(string path)
        {
            WorkingDirectory = path;
            AssertOwnedPath();
            Assert.IsFalse(File.Exists(Path.Combine(path, "workflow.json")));
            Assert.AreEqual(0, Directory.GetFiles(path, "*.dpapi").Length);
        }
        private void AssertOwnedPath()
        {
            Assert.AreEqual(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "IndexIntegrityChecker")), Path.GetDirectoryName(WorkingDirectory));
            Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(WorkingDirectory), "N", out _));
        }
        public void Dispose()
        {
            _held?.Dispose();
            if (WorkingDirectory != null && Directory.Exists(WorkingDirectory))
            {
                AssertOwnedPath();
                Directory.Delete(WorkingDirectory, recursive: true);
            }
        }
    }
}
