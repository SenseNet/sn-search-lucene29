using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lucene.Net.Documents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class DifferentDocumentsTests
{
    [DataTestMethod]
    [DataRow("fields", 5L, 1L)]
    [DataRow("versions", 4L, 2L)]
    [DataRow("missing", 2L, 2L)]
    [DataRow("missing-only", 1L, 1L)]
    [DataRow("orphan-duplicates", 3L, 2L)]
    [DataRow("duplicates", 3L, 2L)]
    [DataRow("issues-and-compare", 3L, 1L)]
    [DataRow("no-version", 2L, 2L)]
    [DataRow("multi-version", 2L, 1L)]
    [DataRow("paths", 4L, 2L)]
    [DataRow("consistent", 0L, 0L)]
    [DataRow("empty", 0L, 0L)]
    public async Task SummaryCountsDistinctAffectedDocumentsAcrossAllDifferenceKinds(string scenario, long differences, long documents)
    {
        using var fixture = new WorkflowTests.Fixture();
        var source = fixture.Operations;
        switch (scenario)
        {
            case "fields":
                source.Documents = new[] { CheckerTests.Doc(node: 2, nodeTs: 101, versionTs: 201, isPublic: false) };
                source.Rows = new[] { CheckerTests.Db(lastDraft: null) };
                break;
            case "versions":
                source.Documents = new[] {
                    CheckerTests.Doc(9, nodeTs: 101, versionTs: 201, isPublic: false, isDraft: false, path: "/old"),
                    CheckerTests.Doc(nodeTs: 101, versionTs: 201) };
                source.Rows = new[] { CheckerTests.Db(9), CheckerTests.Db() };
                break;
            case "missing":
                source.Rows = new[] { CheckerTests.Db(5), CheckerTests.Db(6), CheckerTests.Db() };
                break;
            case "missing-only":
                source.Documents = Array.Empty<Document>();
                break;
            case "orphan-duplicates":
                source.Documents = new[] { CheckerTests.Doc(), CheckerTests.Doc() };
                source.Rows = Array.Empty<DatabaseVersion>();
                break;
            case "duplicates":
                source.Documents = new[] { CheckerTests.Doc(), CheckerTests.Doc(versionTs: 201), CheckerTests.Doc() };
                break;
            case "issues-and-compare":
                var malformed = CheckerTests.Doc(versionTs: 201);
                malformed.RemoveField("NodeTimestamp"); malformed.RemoveField("IsLastPublic");
                source.Documents = new[] { malformed };
                break;
            case "no-version":
                var missing = CheckerTests.Doc(); missing.RemoveField("VersionId");
                source.Documents = new[] { missing };
                break;
            case "multi-version":
                var multiple = CheckerTests.Doc();
                multiple.Add(new NumericField("VersionId", Field.Store.YES, true).SetIntValue(11));
                source.Documents = new[] { multiple };
                break;
            case "paths":
                source.Documents = new[] {
                    CheckerTests.Doc(9, nodeTs: 99, isPublic: false, isDraft: false, path: null),
                    CheckerTests.Doc(versionTs: 201, path: null) };
                source.Rows = new[] { CheckerTests.Db(9), CheckerTests.Db() };
                break;
            case "empty":
                source.Documents = Array.Empty<Document>();
                source.Rows = Array.Empty<DatabaseVersion>();
                break;
        }
        var result = await fixture.RunNew();
        Assert.AreEqual(differences, result.Comparison!.DifferenceCount);
        Assert.AreEqual((long?)documents, result.Comparison.DifferentDocuments);
        using (var summary = JsonDocument.Parse(File.ReadLines(Path.Combine(fixture.Root, result.ReportFile)).Last()))
        {
            Assert.AreEqual(differences, summary.RootElement.GetProperty("differences").GetInt64());
            Assert.AreEqual(documents, summary.RootElement.GetProperty("differentDocuments").GetInt64());
        }
        Assert.AreEqual((long?)documents, fixture.Load().Comparison!.DifferentDocuments);
        fixture.Operations.Unavailable = true;
        using var output = new StringWriter();
        Assert.AreEqual(differences == 0 ? 0 : 1, await Program.ExecuteAsync(new(null, null, fixture.Root),
            fixture.Operations, fixture.Protector, output, TextWriter.Null, default));
        var consoleSummary = TemporaryModeTests.SummaryValues(output.ToString());
        Assert.AreEqual(documents.ToString(), consoleSummary["differentDocuments"]);
    }

    [TestMethod]
    public void DiskCounterDeduplicatesNonAdjacentDocIdsAndKeepsMissingVersionsSeparate()
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "documents.bits");
        using (var count = new DifferentDocumentCounter(path, 1_000_010))
        {
            count.Observe(new(DifferenceKind.MissingField, DocId: 1_000_009));
            count.Observe(new(DifferenceKind.DifferentVersionTimestamp, DocId: 0));
            count.Observe(new(DifferenceKind.MoreDocument, DocId: 1_000_009));
            count.Observe(new(DifferenceKind.NotInIndex, VersionId: 1_000_009));
            count.Observe(new(DifferenceKind.NotInIndex, VersionId: 1_000_009));
            Assert.AreEqual(3L, count.Count);
            Assert.AreEqual(125_002L, new FileInfo(path).Length);
        }
        Assert.IsFalse(File.Exists(path));
    }

    [DataTestMethod]
    [DataRow("ComparisonSaved")]
    [DataRow("Completed")]
    public async Task ExistingReportSummaryIsEnrichedOfflineAndThenReused(string checkpoint)
    {
        using var fixture = new WorkflowTests.Fixture();
        fixture.Operations.Documents = new[] { CheckerTests.Doc(nodeTs: 101, versionTs: 201) };
        await fixture.StopAt(Enum.Parse<WorkflowPhase>(checkpoint));
        var oldState = RemoveDocumentCount(fixture);
        var oldPath = Path.Combine(fixture.Root, oldState.ReportFile);
        var differences = WorkflowTests.ReadDifferences(oldPath).Select(Serialize).ToArray();
        using var oldSummary = JsonDocument.Parse(File.ReadLines(oldPath).Last());
        var elapsed = oldSummary.RootElement.GetProperty("elapsedMilliseconds").GetInt64();
        fixture.Operations.Unavailable = true; fixture.Protector.Unavailable = true;
        var resumed = await fixture.Resume();
        Assert.AreEqual(WorkflowPhase.Completed, resumed.Phase);
        Assert.AreEqual((long?)1, resumed.Comparison!.DifferentDocuments);
        Assert.AreEqual(2L, resumed.Comparison.DifferenceCount);
        var path = Path.Combine(fixture.Root, resumed.ReportFile);
        CollectionAssert.AreEqual(differences, WorkflowTests.ReadDifferences(path).Select(Serialize).ToArray());
        using var summary = JsonDocument.Parse(File.ReadLines(path).Last());
        Assert.AreEqual(1L, summary.RootElement.GetProperty("differentDocuments").GetInt64());
        Assert.AreEqual(elapsed, summary.RootElement.GetProperty("elapsedMilliseconds").GetInt64());
        Assert.IsFalse(File.Exists(oldPath));
        var bytes = File.ReadAllBytes(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        Assert.AreEqual((long?)1, (await fixture.Resume()).Comparison!.DifferentDocuments);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [TestMethod]
    public async Task InterruptedSummaryStateUpdatePreservesCompletedReportAndAllowsRetry()
    {
        using var fixture = new WorkflowTests.Fixture();
        fixture.Operations.Documents = new[] { CheckerTests.Doc(versionTs: 201) };
        await fixture.RunNew();
        var state = RemoveDocumentCount(fixture);
        var path = Path.Combine(fixture.Root, state.ReportFile);
        var bytes = File.ReadAllBytes(path);
        // Block the atomic state write after the replacement report has been completed.
        var blockedState = Path.Combine(fixture.Root, "workflow.json.partial");
        Directory.CreateDirectory(blockedState);
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => fixture.Resume());
        Assert.IsNull(fixture.Load().Comparison!.DifferentDocuments);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Directory.Delete(blockedState);
        fixture.Operations.Unavailable = true; fixture.Protector.Unavailable = true;
        Assert.AreEqual((long?)1, (await fixture.Resume()).Comparison!.DifferentDocuments);
    }

    private static string Serialize(Difference difference) => JsonSerializer.Serialize(difference, JsonOutput.Options);

    private static WorkflowState RemoveDocumentCount(WorkflowTests.Fixture fixture)
    {
        using var workspace = new WorkflowWorkspace(fixture.Root);
        var state = workspace.Load()!;
        var path = workspace.FilePath(state.ReportFile);
        var lines = File.ReadAllLines(path);
        var summary = JsonNode.Parse(lines[^1])!.AsObject();
        summary.Remove("differentDocuments");
        lines[^1] = summary.ToJsonString(JsonOutput.Options);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        var report = new SavedFile(Path.GetFileName(path), new FileInfo(path).Length, lines.Length,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        state.Comparison = state.Comparison! with { Report = report, DifferentDocuments = null };
        workspace.Save(state);
        return state;
    }
}
