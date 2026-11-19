using System.Runtime.CompilerServices;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class CheckerTests
{
    internal static Document Doc(int version = 10, int node = 1, long nodeTs = 100, long versionTs = 200,
        bool isPublic = true, bool isDraft = true, string? path = "/root/a")
    {
        var document = new Document();
        document.Add(new NumericField("VersionId", Field.Store.YES, true).SetIntValue(version));
        document.Add(new NumericField("Id", Field.Store.YES, true).SetIntValue(node));
        document.Add(new NumericField("NodeTimestamp", Field.Store.YES, true).SetLongValue(nodeTs));
        document.Add(new NumericField("VersionTimestamp", Field.Store.YES, true).SetLongValue(versionTs));
        document.Add(new Field("IsLastPublic", isPublic ? "yes" : "no", Field.Store.YES, Field.Index.NOT_ANALYZED));
        document.Add(new Field("IsLastDraft", isDraft ? "yes" : "no", Field.Store.YES, Field.Index.NOT_ANALYZED));
        if (path != null) document.Add(new Field("Path", path, Field.Store.YES, Field.Index.NOT_ANALYZED));
        return document;
    }
    internal static DatabaseVersion Db(int version = 10, int node = 1, long nodeTs = 100, long versionTs = 200,
        int? lastPublic = 10, int? lastDraft = 10) => new(version, versionTs, node, nodeTs, lastPublic, lastDraft);

    internal static async IAsyncEnumerable<DatabaseVersion> Rows(IEnumerable<DatabaseVersion> rows,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        await Task.CompletedTask;
        foreach (var row in rows) { cancel.ThrowIfCancellationRequested(); yield return row; }
    }

    internal static async Task<(List<Difference> Diffs, CheckCounts Counts)> Check(Document[] documents,
        DatabaseVersion[] rows, int? deleteVersion = null)
    {
        using var directory = new RAMDirectory();
        var writer = new IndexWriter(directory, new KeywordAnalyzer(), true, IndexWriter.MaxFieldLength.UNLIMITED);
        foreach (var document in documents) writer.AddDocument(document);
        if (deleteVersion.HasValue) writer.DeleteDocuments(new Term("VersionId", NumericUtils.IntToPrefixCoded(deleteVersion.Value)));
        writer.Close();
        using var reader = IndexReader.Open(directory, true);
        var differences = new List<Difference>();
        var reporter = new DifferenceReporter(differences.Add);
        using var snapshot = new LuceneSnapshot(reader, reporter);
        var counts = await IntegrityChecker.CheckAsync(snapshot, Rows(rows), reporter);
        return (differences, counts);
    }

    [TestMethod]
    public async Task EqualSnapshotsHaveNoDifferences()
    {
        var result = await Check(new[] { Doc() }, new[] { Db() });
        Assert.AreEqual(0, result.Diffs.Count);
        Assert.AreEqual(new CheckCounts(1, 1, 1), result.Counts);
    }

    [TestMethod]
    public async Task EmptyIndexAndEmptyDatabaseAreConsistent()
    {
        var result = await Check(Array.Empty<Document>(), Array.Empty<DatabaseVersion>());
        Assert.AreEqual(0, result.Diffs.Count);
        Assert.AreEqual(new CheckCounts(0, 0, 0), result.Counts);
    }

    [TestMethod]
    public async Task MergeDetectsBothMissingSidesAndTails()
    {
        var result = await Check(new[] { Doc(30, 3), Doc(10), Doc(50, 5) }, new[] { Db(5), Db(), Db(40) });
        CollectionAssert.AreEquivalent(new[] { 5, 40 }, result.Diffs.Where(d => d.Kind == DifferenceKind.NotInIndex).Select(d => d.VersionId!.Value).ToArray());
        CollectionAssert.AreEquivalent(new[] { 30, 50 }, result.Diffs.Where(d => d.Kind == DifferenceKind.NotInDatabase).Select(d => d.VersionId!.Value).ToArray());
        Assert.AreEqual(new CheckCounts(3, 3, 1), result.Counts);
    }

    [TestMethod]
    public async Task EveryDuplicateIsRetainedAndCompared()
    {
        var result = await Check(new[] { Doc(), Doc(versionTs: 201), Doc() }, new[] { Db() });
        Assert.AreEqual(2, result.Diffs.Count(d => d.Kind == DifferenceKind.MoreDocument));
        Assert.AreEqual(1, result.Diffs.Count(d => d.Kind == DifferenceKind.DifferentVersionTimestamp));
        Assert.IsFalse(result.Diffs.Any(d => d.Kind == DifferenceKind.NotInDatabase));
        Assert.AreEqual(new CheckCounts(1, 3, 3), result.Counts);
    }

    [TestMethod]
    public async Task OrphanDuplicatesAreReportedAsBothDuplicateAndOrphan()
    {
        var result = await Check(new[] { Doc(), Doc() }, Array.Empty<DatabaseVersion>());
        Assert.AreEqual(1, result.Diffs.Count(d => d.Kind == DifferenceKind.MoreDocument));
        Assert.AreEqual(2, result.Diffs.Count(d => d.Kind == DifferenceKind.NotInDatabase));
    }

    [TestMethod]
    public async Task ChecksAllMetadataFields()
    {
        var result = await Check(new[] { Doc(node: 2, nodeTs: 101, versionTs: 201, isPublic: false) },
            new[] { Db(lastDraft: null) });
        CollectionAssert.AreEquivalent(new[] { DifferenceKind.DifferentNodeId, DifferenceKind.DifferentNodeTimestamp,
            DifferenceKind.DifferentVersionTimestamp, DifferenceKind.DifferentLastPublicFlag, DifferenceKind.DifferentLastDraftFlag },
            result.Diffs.Select(d => d.Kind).ToArray());
    }

    [TestMethod]
    public async Task HistoricalNodeTimestampIsAllowedOnlyWhenDraftPathMatches()
    {
        var result = await Check(new[] { Doc(9, nodeTs: 99, isPublic: false, isDraft: false), Doc() },
            new[] { Db(9), Db() });
        Assert.AreEqual(0, result.Diffs.Count);
    }

    [TestMethod]
    public async Task HistoricalPathChangeIsReported()
    {
        var result = await Check(new[] { Doc(9, nodeTs: 99, isPublic: false, isDraft: false, path: "/root/old"), Doc() },
            new[] { Db(9), Db() });
        Assert.AreEqual(DifferenceKind.DifferentNodeTimestamp, result.Diffs.Single().Kind);
        Assert.AreEqual(9, result.Diffs.Single().VersionId);
    }

    [TestMethod]
    public async Task MissingDraftDoesNotHideHistoricalTimestampDifference()
    {
        var result = await Check(new[] { Doc(9, nodeTs: 99, isPublic: false, isDraft: false) }, new[] { Db(9) });
        Assert.AreEqual(DifferenceKind.DifferentNodeTimestamp, result.Diffs.Single().Kind);
    }

    [TestMethod]
    public async Task MissingPathsDoNotSuppressTimestampDifference()
    {
        var result = await Check(new[] { Doc(9, nodeTs: 99, isPublic: false, isDraft: false, path: null), Doc(path: null) },
            new[] { Db(9), Db() });
        Assert.AreEqual(2, result.Diffs.Count(d => d.Kind == DifferenceKind.MissingField && d.Field == "Path"));
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.DifferentNodeTimestamp));
    }

    [TestMethod]
    public async Task DeletedDocumentsAndCommitMarkersAreExcluded()
    {
        var commit = new Document();
        commit.Add(new Field("$#COMMIT", "$#COMMIT", Field.Store.YES, Field.Index.NOT_ANALYZED));
        var result = await Check(new[] { Doc(9), commit, Doc() }, new[] { Db() }, deleteVersion: 9);
        Assert.AreEqual(0, result.Diffs.Count);
        Assert.AreEqual(new CheckCounts(1, 1, 1), result.Counts);
    }

    [TestMethod]
    public async Task DocumentWithoutVersionIdIsNotSilentlyOmitted()
    {
        var document = Doc();
        document.RemoveField("VersionId");
        var result = await Check(new[] { document }, new[] { Db() });
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.MissingField && d.Field == "VersionId"));
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.NotInIndex));
    }

    [DataTestMethod]
    [DataRow("Id")]
    [DataRow("VersionTimestamp")]
    [DataRow("NodeTimestamp")]
    [DataRow("IsLastPublic")]
    [DataRow("IsLastDraft")]
    public async Task MissingMetadataIsReported(string field)
    {
        var document = Doc(); document.RemoveField(field);
        var result = await Check(new[] { document }, new[] { Db() });
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.MissingField && d.Field == field));
    }

    [TestMethod]
    public async Task MultipleValuesInSingleValuedFieldAreReported()
    {
        var document = Doc();
        document.Add(new NumericField("VersionTimestamp", Field.Store.YES, true).SetLongValue(999));
        document.Add(new NumericField("VersionId", Field.Store.YES, true).SetIntValue(11));
        var result = await Check(new[] { document }, new[] { Db() });
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.MultipleFieldValues && d.Field == "VersionTimestamp"));
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.MultipleFieldValues && d.Field == "VersionId"));
    }

    [TestMethod]
    public async Task InvalidBooleanIsReported()
    {
        var document = Doc(); document.RemoveField("IsLastDraft");
        document.Add(new Field("IsLastDraft", "invalid", Field.Store.YES, Field.Index.NOT_ANALYZED));
        var result = await Check(new[] { document }, new[] { Db() });
        Assert.IsTrue(result.Diffs.Any(d => d.Kind == DifferenceKind.InvalidField && d.Field == "IsLastDraft"));
    }

    [TestMethod]
    public async Task NumericTermsOrderIdsAndIgnoreReducedPrecisionTokensAcrossSegments()
    {
        using var directory = new RAMDirectory();
        var writer = new IndexWriter(directory, new KeywordAnalyzer(), true, IndexWriter.MaxFieldLength.UNLIMITED);
        int[] ids = { int.MaxValue, 1, 130, 128, 127, 4000, 10 };
        foreach (var id in ids) { writer.AddDocument(Doc(id, id, isPublic: false, isDraft: false)); writer.Commit(); }
        writer.Close();
        using var reader = IndexReader.Open(directory, true);
        var diffs = new List<Difference>(); var reporter = new DifferenceReporter(diffs.Add);
        using var snapshot = new LuceneSnapshot(reader, reporter);
        var result = await IntegrityChecker.CheckAsync(snapshot,
            Rows(ids.Order().Select(id => Db(id, id, lastPublic: null, lastDraft: null))), reporter);
        Assert.AreEqual(0, diffs.Count);
        Assert.AreEqual((long)ids.Length, result.IndexDocuments);
    }

    [TestMethod]
    public async Task UnsortedDatabaseInputFailsInsteadOfReportingSuccess()
    {
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Check(Array.Empty<Document>(), new[] { Db(20), Db(10) }));
    }

    [TestMethod]
    public void CancellationStopsExtraction()
    {
        using var directory = new RAMDirectory();
        var writer = new IndexWriter(directory, new KeywordAnalyzer(), true, IndexWriter.MaxFieldLength.UNLIMITED);
        writer.AddDocument(Doc()); writer.Close();
        using var reader = IndexReader.Open(directory, true);
        Assert.ThrowsException<OperationCanceledException>(() =>
            new LuceneSnapshot(reader, new DifferenceReporter(_ => { }), new CancellationToken(true)));
    }
}
