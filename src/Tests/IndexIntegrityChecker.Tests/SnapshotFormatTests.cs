using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class SnapshotFormatTests
{
    private const string DbHeader = "nodeId\tversionId\tnodeTimestamp\tversionTimestamp\tlastMajorVersionId\tlastMinorVersionId";
    private const string IxHeader = "nodeId\tversionId\tnodeTimestamp\tversionTimestamp\tisLastPublic\tisLastDraft\tdocId\tpresentFields\tdraftDocId\thistoricalPathMatches\tmissingOwnPath\tmissingDraftPath";
    private const string DbLine = "1\t10\t100\t200\t10\t10";
    private static readonly JsonSerializerOptions ImportOptions = new(JsonOutput.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    [TestMethod]
    public void DatabaseTsvPreservesColumnOrderNullsAndInt64Precision()
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "database.tsv");
        var expected = new[] {
            new DatabaseVersion(1, long.MaxValue, 7, long.MinValue, null, 1),
            new DatabaseVersion(2, 9_007_199_254_740_993L, 8, 42, 0, null),
            new DatabaseVersion(3, 0, 9, 0, null, null)
        };
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "NEG";
            CultureInfo.CurrentCulture = culture;
            using (var output = new CheckpointWriter(path))
            {
                using (var writer = SnapshotFiles.StartDatabase(output.Stream))
                    foreach (var row in expected) SnapshotFiles.Write(writer, row);
                output.Complete(expected.Length, default);
            }
            CollectionAssert.AreEqual(expected, SnapshotFiles.ReadDatabase(path, 3, default).ToArray());
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        var bytes = File.ReadAllBytes(path);
        Assert.AreEqual((byte)'n', bytes[0]); // No BOM.
        Assert.IsFalse(bytes.Contains((byte)'\r'));
        CollectionAssert.AreEqual(new[] {
            DbHeader,
            "7\t1\t-9223372036854775808\t9223372036854775807\t\t1",
            "8\t2\t42\t9007199254740993\t0\t",
            "9\t3\t0\t0\t\t"
        }, File.ReadAllLines(path));
    }

    [TestMethod]
    public void IndexTsvPreservesColumnOrderMissingFieldsAndPathMetadata()
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "index.tsv");
        var expected = new[] {
            new SavedIndexVersion(1, 0, new IndexMetadata { NodeId = 10, NodeTimestamp = long.MinValue,
                VersionTimestamp = long.MaxValue, Present = Fields.Metadata | Fields.VersionId,
                IsLastPublic = true, HistoricalPathMatches = true, DraftDocId = 1 }),
            new SavedIndexVersion(2, 1, new IndexMetadata { NodeId = 10, Present = Fields.VersionId | Fields.NodeId,
                IsLastDraft = true, MissingOwnPath = true, MissingDraftPath = true, DraftDocId = 0 })
        };
        using (var output = new CheckpointWriter(path))
        {
            using (var writer = SnapshotFiles.StartIndex(output.Stream))
                foreach (var row in expected) SnapshotFiles.Write(writer, row);
            output.Complete(expected.Length, default);
        }
        CollectionAssert.AreEqual(new[] {
            IxHeader,
            "10\t1\t-9223372036854775808\t9223372036854775807\t1\t0\t0\t63\t1\t1\t0\t0",
            "10\t2\t0\t0\t0\t1\t1\t33\t0\t0\t1\t1"
        }, File.ReadAllLines(path));
        CollectionAssert.AreEqual(expected, SnapshotFiles.ReadIndex(path, 2, 2, default).ToArray());
    }

    [DataTestMethod]
    [DataRow("missing-header")]
    [DataRow("wrong-header")]
    [DataRow("missing-row")]
    [DataRow("extra-row")]
    [DataRow("blank-row")]
    [DataRow("missing-field")]
    [DataRow("extra-field")]
    [DataRow("wrong-type")]
    [DataRow("overflow")]
    [DataRow("empty-required")]
    [DataRow("text-null")]
    public void InvalidTsvInputIsRejected(string corruption)
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "database.tsv");
        var content = corruption switch
        {
            "missing-header" => DbLine + "\n",
            "wrong-header" => DbHeader.Replace("nodeId\tversionId", "versionId\tnodeId", StringComparison.Ordinal) + "\n" + DbLine + "\n",
            "missing-row" => DbHeader + "\n",
            "extra-row" => DbHeader + "\n" + DbLine + "\n" + DbLine + "\n",
            "blank-row" => DbHeader + "\n\n",
            "missing-field" => DbHeader + "\n1\t10\t100\t200\t10\n",
            "extra-field" => DbHeader + "\n" + DbLine + "\t0\n",
            "wrong-type" => DbHeader + "\n1\t10\t100\t2.5\t10\t10\n",
            "overflow" => DbHeader + "\n1\t10\t100\t9223372036854775808\t10\t10\n",
            "empty-required" => DbHeader + "\n1\t10\t\t200\t10\t10\n",
            "text-null" => DbHeader + "\n1\t10\t100\t200\tnull\t10\n",
            _ => throw new InvalidOperationException()
        };
        File.WriteAllText(path, content, new UTF8Encoding(false));
        Assert.ThrowsException<InvalidDataException>(() => SnapshotFiles.ReadDatabase(path, 1, default).ToArray());
    }

    [DataTestMethod]
    [DataRow(4, "2")]
    [DataRow(5, "true")]
    [DataRow(7, "64")]
    [DataRow(7, "319")]
    [DataRow(7, "31")]
    [DataRow(6, "-1")]
    [DataRow(6, "2")]
    [DataRow(8, "2")]
    [DataRow(9, "-1")]
    [DataRow(10, "yes")]
    [DataRow(11, "")]
    public void InvalidIndexFlagsAndDocumentReferencesAreRejected(int column, string value)
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "index.tsv");
        var columns = "1\t10\t100\t200\t0\t0\t0\t63\t1\t0\t0\t1".Split('\t');
        columns[column] = value;
        File.WriteAllText(path, IxHeader + "\n" + string.Join('\t', columns) + "\n", new UTF8Encoding(false));
        Assert.ThrowsException<InvalidDataException>(() => SnapshotFiles.ReadIndex(path, 1, 2, default).ToArray());
    }

    [TestMethod]
    public void TsvReadingObservesCancellationBetweenRecords()
    {
        using var fixture = new WorkflowTests.Fixture();
        var path = Path.Combine(fixture.Root, "database.tsv");
        File.WriteAllText(path, DbHeader + "\n" + DbLine + "\n1\t20\t100\t200\t20\t20\n", new UTF8Encoding(false));
        using var cancel = new CancellationTokenSource();
        using var rows = SnapshotFiles.ReadDatabase(path, 2, cancel.Token).GetEnumerator();
        Assert.IsTrue(rows.MoveNext());
        cancel.Cancel();
        Assert.ThrowsException<OperationCanceledException>(() => rows.MoveNext());
    }

    [DataTestMethod]
    [DataRow(1, "Initialized")]
    [DataRow(1, "DatabaseSaved")]
    [DataRow(1, "IndexSaved")]
    [DataRow(1, "ComparisonSaved")]
    [DataRow(1, "Completed")]
    [DataRow(2, "Initialized")]
    [DataRow(2, "DatabaseSaved")]
    [DataRow(2, "IndexSaved")]
    [DataRow(2, "ComparisonSaved")]
    [DataRow(2, "Completed")]
    public async Task SupportedInputCheckpointsAreConvertedAndResumedFromEveryPhase(int format, string checkpoint)
    {
        using var fixture = new WorkflowTests.Fixture();
        var phase = Enum.Parse<WorkflowPhase>(checkpoint);
        await fixture.StopAt(phase);
        var saved = RewriteAsFormat(fixture, format);
        var reportPath = Path.Combine(fixture.Root, saved.ReportFile);
        var report = File.Exists(reportPath) ? File.ReadAllBytes(reportPath) : null;
        if (phase >= WorkflowPhase.DatabaseSaved) fixture.Protector.Unavailable = true;
        if (phase >= WorkflowPhase.IndexSaved) fixture.Operations.Unavailable = true;
        var result = await fixture.Resume();
        Assert.AreEqual(3, result.FormatVersion);
        Assert.AreEqual(WorkflowPhase.Completed, result.Phase);
        Assert.AreEqual(new CheckCounts(1, 1, 1), result.Comparison!.Counts);
        Assert.AreEqual(1, fixture.Operations.DatabaseCalls);
        Assert.AreEqual(1, fixture.Operations.IndexCalls);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, saved.DatabaseFile)));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, saved.IndexFile)));
        CollectionAssert.AreEqual(fixture.Operations.Rows, SnapshotFiles.ReadDatabase(
            Path.Combine(fixture.Root, result.DatabaseFile), result.Database!.Data.Rows, default).ToArray());
        if (report != null) CollectionAssert.AreEqual(report, File.ReadAllBytes(reportPath));
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task FailedConversionPreservesInputCheckpointAndCanBeRetried(int format)
    {
        using var fixture = new WorkflowTests.Fixture();
        await fixture.StopAt(WorkflowPhase.IndexSaved);
        var state = RewriteAsFormat(fixture, format);
        var indexPath = Path.Combine(fixture.Root, state.IndexFile);
        var indexBytes = File.ReadAllBytes(indexPath);
        // Valid checksum, invalid structure: failure after database conversion has completed.
        File.WriteAllBytes(indexPath, new byte[] { 1, 2, 3 });
        using (var workspace = new WorkflowWorkspace(fixture.Root))
        {
            state.IndexSnapshot = state.IndexSnapshot! with { Data = Describe(indexPath, state.IndexSnapshot!.Data.Rows) };
            workspace.Save(state);
        }
        fixture.Operations.Unavailable = true; fixture.Protector.Unavailable = true;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => fixture.Resume());
        Assert.AreEqual(format, fixture.Load().FormatVersion);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, state.DatabaseFile)));
        Assert.IsTrue(File.Exists(indexPath));
        File.WriteAllBytes(indexPath, indexBytes);
        using (var workspace = new WorkflowWorkspace(fixture.Root))
        {
            state.IndexSnapshot = state.IndexSnapshot! with { Data = Describe(indexPath, state.IndexSnapshot!.Data.Rows) };
            workspace.Save(state);
        }
        var result = await fixture.Resume();
        Assert.AreEqual(3, result.FormatVersion);
        Assert.AreEqual(0L, result.Comparison!.DifferenceCount);
    }

    private static WorkflowState RewriteAsFormat(WorkflowTests.Fixture fixture, int format)
    {
        using var workspace = new WorkflowWorkspace(fixture.Root);
        var state = workspace.Load()!;
        var dbPath = workspace.FilePath(state.DatabaseFile);
        var ixPath = workspace.FilePath(state.IndexFile);
        var database = state.Phase >= WorkflowPhase.DatabaseSaved
            ? SnapshotFiles.ReadDatabase(dbPath, state.Database!.Data.Rows, default).ToArray() : null;
        var index = state.Phase >= WorkflowPhase.IndexSaved
            ? SnapshotFiles.ReadIndex(ixPath, state.IndexSnapshot!.Data.Rows, state.IndexSnapshot.MaxDoc, default).ToArray() : null;
        state.FormatVersion = format;
        if (database != null)
        {
            var path = workspace.FilePath(state.DatabaseFile);
            if (format == 1)
            {
                using var writer = new BinaryWriter(File.Create(path));
                writer.Write(Encoding.ASCII.GetBytes("SNICDB01")); writer.Write(32); writer.Write(1);
                foreach (var row in database)
                {
                    writer.Write(row.VersionId); writer.Write(row.NodeId); writer.Write(row.VersionTimestamp);
                    writer.Write(row.NodeTimestamp); writer.Write(row.LastMajorVersionId ?? 0); writer.Write(row.LastMinorVersionId ?? 0);
                }
            }
            else File.WriteAllLines(path, database.Select(row => JsonSerializer.Serialize(row, ImportOptions)), new UTF8Encoding(false));
            state.Database = state.Database! with { Data = Describe(path, database.Length) };
            File.Delete(dbPath);
        }
        if (index != null)
        {
            var path = workspace.FilePath(state.IndexFile);
            if (format == 1)
            {
                using var writer = new BinaryWriter(File.Create(path));
                writer.Write(Encoding.ASCII.GetBytes("SNICIX01")); writer.Write(40); writer.Write(1);
                foreach (var row in index)
                {
                    var data = row.Metadata;
                    writer.Write(row.VersionId); writer.Write(row.DocId); writer.Write(data.NodeId); writer.Write(data.DraftDocId);
                    writer.Write(data.NodeTimestamp); writer.Write(data.VersionTimestamp); writer.Write((byte)data.Present);
                    writer.Write((byte)((data.IsLastPublic ? 1 : 0) | (data.IsLastDraft ? 2 : 0) |
                        (data.HistoricalPathMatches ? 4 : 0) | (data.MissingOwnPath ? 8 : 0) | (data.MissingDraftPath ? 16 : 0)));
                    writer.Write((short)0); writer.Write(0);
                }
            }
            else File.WriteAllLines(path, index.Select(row => JsonSerializer.Serialize(new
            {
                row.VersionId, row.DocId, row.Metadata.NodeId, row.Metadata.NodeTimestamp, row.Metadata.VersionTimestamp,
                row.Metadata.IsLastPublic, row.Metadata.IsLastDraft, presentFields = row.Metadata.Present,
                row.Metadata.DraftDocId, row.Metadata.HistoricalPathMatches, row.Metadata.MissingOwnPath, row.Metadata.MissingDraftPath
            }, ImportOptions)), new UTF8Encoding(false));
            state.IndexSnapshot = state.IndexSnapshot! with { Data = Describe(path, index.Length) };
            File.Delete(ixPath);
        }
        workspace.Save(state);
        return state;
    }

    private static SavedFile Describe(string path, long rows) =>
        new(Path.GetFileName(path), new FileInfo(path).Length, rows, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
