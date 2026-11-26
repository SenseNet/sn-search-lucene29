using Lucene.Net.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;

namespace IndexIntegrityChecker.Tests;

[TestClass]
public class OnlineIntegrityTests
{
    [TestMethod]
    public void LiveCheckPinsCommitAndRepairSurvivesAnUnrelatedCommit()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"integrity-lucene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var manager = new LuceneSearchManager(new IndexDirectory("0", directory));
        manager.SetIndexingInfo(new Dictionary<string, Analyzer>(), new Dictionary<string, IndexValueType>
        {
            [IndexFieldName.VersionId] = IndexValueType.Int
        });
        manager.Start(TextWriter.Null);
        try
        {
            manager.WriteIndex(null, null, new[] { Document(42) });
            manager.WriteActivityStatusToIndex(new IndexingActivityStatus { LastActivityId = 10 });
            using (var check = manager.OpenIntegrityReader(CancellationToken.None))
            {
                manager.WriteIndex(null, null, new[] { Document(43) });
                manager.WriteActivityStatusToIndex(new IndexingActivityStatus { LastActivityId = 12, Gaps = [11] });
                var rows = new List<SavedIndexVersion>();
                check.Export(directory, rows.Add, _ => { }, CancellationToken.None);
                CollectionAssert.AreEqual(new[] { 42 }, rows.Select(row => row.VersionId).ToArray());
                Assert.AreEqual(10, check.Activity.LastActivityId);
            }

            var probe = manager.ProbeIntegrity(new[] { 42, 43 }, directory, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { 42, 43 }, probe.Documents.Select(row => row.VersionId).ToArray());
            manager.WriteActivityStatusToIndex(new IndexingActivityStatus { LastActivityId = 14, Gaps = [11, 13] });
            Assert.IsTrue(manager.TryApplyIntegrityRepairs(probe.Revision,
                new IndexIntegrityUpdate[] { new(42, null), new(43, Document(43)) }, CancellationToken.None));

            var rerun = manager.ProbeIntegrity(new[] { 42, 43 }, directory, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { 43 }, rerun.Documents.Select(row => row.VersionId).ToArray());
            var status = manager.ReadActivityStatusFromIndex();
            Assert.AreEqual(14, status.LastActivityId);
            CollectionAssert.AreEqual(new[] { 11, 13 }, status.Gaps);

            manager.WriteIndex(null, null, new[] { Document(44) });
            Assert.IsFalse(manager.TryApplyIntegrityRepairs(rerun.Revision,
                new IndexIntegrityUpdate[] { new(43, null) }, CancellationToken.None));
        }
        finally
        {
            manager.ShutDown();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IndexDocument Document(int id)
    {
        var document = new IndexDocument();
        document.Add(new IndexField(IndexFieldName.VersionId, id, IndexingMode.AnalyzedNoNorms,
            IndexStoringMode.Yes, IndexTermVector.No));
        return document;
    }
}
