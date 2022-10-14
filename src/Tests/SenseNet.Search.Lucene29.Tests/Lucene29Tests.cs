using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.InMemory;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Search.Indexing;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search.Querying;
using SenseNet.Testing;
using SenseNet.Tests.Core;
using SenseNet.Tests.Core.Implementations;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace SenseNet.Search.Lucene29.Tests
{
    /// <summary>
    /// Test indexing engine implementation for throwing an exception during testing.
    /// </summary>
    internal class Lucene29LocalIndexingEngineFailStartup : Lucene29LocalIndexingEngine
    {
        protected override void Startup(TextWriter consoleOut)
        {
            base.Startup(consoleOut);

            throw new InvalidOperationException("Exception thrown for testing purposes, ignore it.");
        }
    }

    [TestClass]
    public class Lucene29Tests : L29TestBase
    {
        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_BasicConditions()
        {
            await L29Test(() =>
                {
                    var engine = Providers.Instance.IndexManager.IndexingEngine;
                    var indexDir = ((Lucene29LocalIndexingEngine)engine).IndexDirectory.CurrentDirectory;

                    Assert.AreEqual(typeof(Lucene29LocalIndexingEngine).FullName, engine.GetType().FullName);
                    Assert.IsNotNull(indexDir);
                    Assert.IsTrue(indexDir.Contains(nameof(L29_BasicConditions)));

                    return Task.CompletedTask;
                }, false);
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_ClearAndPopulateAll()
        {
            var sb = new StringBuilder();
            IIndexingActivity[] activities;

            await L29Test(async () =>
            {
                using (new SystemAccount())
                {
                    var paths = new List<string>();
                    var populator = Providers.Instance.SearchManager.GetIndexPopulator();
                    populator.NodeIndexed += (sender, e) => { paths.Add(e.Path); };

                    // ACTION
                    using (var console = new StringWriter(sb))
                        await populator.ClearAndPopulateAllAsync(CancellationToken.None, console);

                    // load last indexing activity
                    var db = Providers.Instance.DataProvider;
                    var activityId = await db.GetLastIndexingActivityIdAsync(CancellationToken.None);
                    activities = await db.LoadIndexingActivitiesAsync(1, activityId, 10000, false,
                        new IndexingActivityFactory(), CancellationToken.None);

                    GetAllIdValuesFromIndex(out var nodeIds, out var versionIds);

                    var nodeCount = await Providers.Instance.DataStore.GetNodeCountAsync(CancellationToken.None);
                    var versionCount = await Providers.Instance.DataStore.GetVersionCountAsync(CancellationToken.None);

                    Assert.AreEqual(0, activities.Length);
                    Assert.AreEqual(nodeCount, nodeIds.Length);
                    Assert.AreEqual(versionCount, versionIds.Length);
                    Assert.AreEqual(versionCount, paths.Count);
                }
            });
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Query()
        {
            QueryResult queryResult1, queryResult2;
            await L29Test(async () =>
            {
                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    queryResult1 = CreateSafeContentQuery("Id:1").Execute();
                    queryResult2 = CreateSafeContentQuery("Id:2 .COUNTONLY").Execute();

                    Assert.AreEqual(typeof(Lucene29LocalIndexingEngine).FullName,
                        Providers.Instance.IndexManager.IndexingEngine.GetType().FullName);
                    var indxDir = ((Lucene29LocalIndexingEngine)Providers.Instance.IndexManager.IndexingEngine).IndexDirectory
                        .CurrentDirectory;
                    Assert.IsNotNull(indxDir);
                    Assert.AreEqual(-1, User.Current.Id);
                    Assert.AreEqual(1, queryResult1.Count);
                    Assert.AreEqual(1, queryResult1.Identifiers.FirstOrDefault());
                    Assert.AreEqual(1, queryResult2.Count);
                    Assert.AreEqual(0, queryResult2.Identifiers.FirstOrDefault());
                }
            });
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Query_TopSkipResultCount()
        {
            await L29Test(async () =>
            {
                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    var queryResult = CreateSafeContentQuery("Id:>1 .TOP:10 .SKIP:20 .AUTOFILTERS:OFF").Execute();
                    var identifiers = queryResult.Identifiers.ToArray();
                    Assert.IsTrue(identifiers.Length > 0);
                    Assert.IsTrue(queryResult.Count > identifiers.Length);
                }
            });
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            DeleteIndexDirectories();
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_SaveAndQuery()
        {
            QueryResult queryResultBefore, queryResultAfter;
            await L29Test(async () =>
                {
                    var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                    using (new SystemAccount())
                    {
                        var root = Repository.Root;
                        var nodeName = "NodeForL29_SaveAndQuery";

                        queryResultBefore = CreateSafeContentQuery($"Name:{nodeName}").Execute();

                        var node = new SystemFolder(root) {Name = nodeName};
                        await node.SaveAsync(CancellationToken.None);

                        queryResultAfter = CreateSafeContentQuery($"Name:{nodeName}").Execute();

                        Assert.AreEqual(0, queryResultBefore.Count);
                        Assert.AreEqual(1, queryResultAfter.Count);
                        Assert.IsTrue(node.Id > 0);
                        Assert.AreEqual(node.Id, queryResultAfter.Identifiers.FirstOrDefault());
                    }
                });
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_SaveAndQuery_ModificationDate()
        {
            await L29Test(async () =>
            {
                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    var root = Repository.Root;
                    var nodeName = "L29_SaveAndQuery_ModificationDate";

                    var node = new SystemFolder(root) {Name = nodeName};
                    await node.SaveAsync(CancellationToken.None);

                    var date = node.ModificationDate.AddMinutes(-1.5);
                    var value = date.ToString("yyyy-MM-dd HH:mm:ss");
                    var query = $"+Name:'{nodeName}' +ModificationDate:>'{value}' .TOP:5";

                    var queryResult = CreateSafeContentQuery(query).Execute();

                    Assert.IsTrue(1 <= queryResult.Count);
                }
            });
        }

        //[TestMethod, TestCategory("IR, L29")]
        //public void L29_StartUpFail()
        //{
        //    Assert.Inconclusive("Currently the write.lock cleanup does not work correctly in a test environment.");

        //    var dataProvider = new InMemoryDataProvider();
        //    var securityDataProvider = GetSecurityDataProvider(dataProvider);

        //    // Search engine that contains an indexing engine that will throw 
        //    // an exception during startup to test index directory cleanup.
        //    var searchEngine = new Lucene29SearchEngine
        //    {
        //        IndexingEngine = new Lucene29LocalIndexingEngineFailStartup()
        //    };

        //    Indexing.IsOuterSearchEngineEnabled = true;
        //    CommonComponents.TransactionFactory = dataProvider;
        //    DistributedApplication.Cache.Reset();

        //    var indxManConsole = new StringWriter();
        //    var repoBuilder = new RepositoryBuilder()
        //        .UseDataProvider(dataProvider)
        //        .UseAccessProvider(new DesktopAccessProvider())
        //        .UsePermissionFilterFactory(new EverythingAllowedPermissionFilterFactory())
        //        .UseSearchEngine(searchEngine)
        //        .UseSecurityDataProvider(securityDataProvider)
        //        .UseCacheProvider(new EmptyCache())
        //        .StartWorkflowEngine(false)
        //        .UseTraceCategories(new [] { "Test", "Event", "Repository", "System" });

        //    repoBuilder.Console = indxManConsole;

        //    try
        //    {
        //        using (Repository.Start(repoBuilder))
        //        {
        //            // Although the repo start process fails, the next startup
        //            // should clean the lock file from the index directory.
        //        }
        //    }
        //    catch (InvalidOperationException)
        //    {
        //        // expected
        //    }

        //    // revert to a regular search engine that does not throw an exception
        //    repoBuilder.UseSearchEngine(new Lucene29SearchEngine());

        //    var originalTimeout = Indexing.IndexLockFileWaitForRemovedTimeout;

        //    try
        //    {
        //        // remove lock file after 5 seconds
        //        Indexing.IndexLockFileWaitForRemovedTimeout = 5;

        //        // Start the repo again to check if indexmanager is able to start again correctly.
        //        using (Repository.Start(repoBuilder))
        //        {

        //        }
        //    }
        //    finally
        //    {
        //        Indexing.IndexLockFileWaitForRemovedTimeout = originalTimeout;
        //    }
        //}

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_SwitchOffRunningState()
        {
            RepositoryBuilder repoBuilder = null;

            await L29Test(builder =>
                {
                    // pin the builder object to use it later again
                    repoBuilder = builder;
                },
                () =>
                {
                    // Switch off the running flag. The shutdown mechanism
                    // should still clean up the index directory.
                    ((Lucene29LocalIndexingEngine) Providers.Instance.SearchEngine.IndexingEngine).Running = false;

                    return Task.CompletedTask;
                }, 
                false);

            repoBuilder.InitialData = null; // (hack :)

            // Start the repo again to check if indexmanager is able to start again correctly.
            using (Repository.Start(repoBuilder))
            {

            }
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_NamedIndexDirectory()
        {
            var folderName = "Test_" + nameof(L29_NamedIndexDirectory);

            await L29Test(() =>
            {
                var expectedPath = Path.Combine(Providers.Instance.SearchManager.IndexDirectoryPath, folderName);
                var indexingEngine = (Lucene29LocalIndexingEngine) Providers.Instance.SearchEngine.IndexingEngine;
                var currentDir = indexingEngine.IndexDirectory.CurrentDirectory;
                var guidText = currentDir.Substring(expectedPath.Length + 1);

                var unused = Guid.Parse(guidText);

                Assert.IsTrue(currentDir.StartsWith(expectedPath + "_"));
                Assert.AreEqual(Path.Combine(currentDir, "write.lock"),
                    indexingEngine.IndexDirectory.IndexLockFilePath);
                Assert.IsTrue(File.Exists(indexingEngine.IndexDirectory.IndexLockFilePath));

                return Task.CompletedTask;
            }, false);
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_ActivityStatus_WithoutSave()
        {
            var newStatus = new IndexingActivityStatus
            {
                LastActivityId = 33,
                Gaps = new[] { 5, 6, 7 }
            };

            await L29Test(async () =>
            {
                var searchEngine = Providers.Instance.SearchManager.SearchEngine;
                var originalStatus = await searchEngine.IndexingEngine.ReadActivityStatusFromIndexAsync(CancellationToken.None);

                await searchEngine.IndexingEngine.WriteActivityStatusToIndexAsync(newStatus, CancellationToken.None);

                var updatedStatus = await searchEngine.IndexingEngine.ReadActivityStatusFromIndexAsync(CancellationToken.None);

                var resultStatus = new IndexingActivityStatus()
                {
                    LastActivityId = updatedStatus.LastActivityId,
                    Gaps = updatedStatus.Gaps
                };

                Assert.AreEqual(originalStatus.LastActivityId, 0);
                Assert.AreEqual(originalStatus.Gaps.Length, 0);
                Assert.AreEqual(newStatus.ToString(), resultStatus.ToString());
            }, false);
        }
        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_ActivityStatus_WithSave()
        {
            await L29Test(async () =>
            {
                var searchEngine = Providers.Instance.SearchManager.SearchEngine;
                var originalStatus = await searchEngine.IndexingEngine.ReadActivityStatusFromIndexAsync(CancellationToken.None);

                using (new SystemAccount())
                {
                    var node = new SystemFolder(Repository.Root) {Name = "L29_ActivityStatus_WithSave"};
                    await node.SaveAsync(CancellationToken.None);
                }

                await Task.Delay(100);

                var updatedStatus = await searchEngine.IndexingEngine.ReadActivityStatusFromIndexAsync(CancellationToken.None);

                Assert.AreEqual(originalStatus.LastActivityId + 1, updatedStatus.LastActivityId);
            }, false);
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Analyzers()
        {
            await L29Test(() =>
            {
//ResetContentTypeManager();
//ContentType.GetByName("GenericContent");
//var analyzersAfter = Providers.Instance.SearchManager.SearchEngine.GetAnalyzers();

                var searchEngine = Providers.Instance.SearchManager.SearchEngine;
                var indexingEngine = (Lucene29LocalIndexingEngine) searchEngine.IndexingEngine;

                var masterAnalyzerAcc = new ObjectAccessor(indexingEngine.GetAnalyzer());

                Assert.AreEqual(typeof(StandardAnalyzer).FullName, ((Analyzer)masterAnalyzerAcc.Invoke("GetAnalyzer", IndexFieldName.AllText)).GetType().FullName);
                Assert.AreEqual(typeof(KeywordAnalyzer).FullName, ((Analyzer)masterAnalyzerAcc.Invoke("GetAnalyzer", IndexFieldName.NodeId)).GetType().FullName);
                Assert.AreEqual(typeof(KeywordAnalyzer).FullName, ((Analyzer)masterAnalyzerAcc.Invoke("GetAnalyzer", IndexFieldName.Name)).GetType().FullName);
                Assert.AreEqual(typeof(StandardAnalyzer).FullName, ((Analyzer)masterAnalyzerAcc.Invoke("GetAnalyzer", "Description")).GetType().FullName);
                Assert.AreEqual(typeof(StandardAnalyzer).FullName, ((Analyzer)masterAnalyzerAcc.Invoke("GetAnalyzer", "Binary")).GetType().FullName);
                Assert.AreEqual(typeof(KeywordAnalyzer).FullName, ((Analyzer)masterAnalyzerAcc.Invoke("GetAnalyzer", "FakeField")).GetType().FullName);

                return Task.CompletedTask;
            }, false);
        }

        /* ======================================================================================= */

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Query_MustAndShould()
        {
            await L29Test(async () =>
            {
                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    var root = Repository.Root;
                    var nameBase = "L29_Query_MustAndShould_";
                    for (var i = 0; i < 4; i++)
                    {
                        var node = new SystemFolder(root)
                            {Name = $"{nameBase}{i}", Description = $"D{i}", Index = 10000 + i};
                        using (new SystemAccount())
                            await node.SaveAsync(CancellationToken.None);
                    }

                    var query = $"+Name:'{nameBase}0' Index:10001";
                    var queryResult = CreateSafeContentQuery(query).Execute();
                    var actual = string.Join(", ",
                        queryResult.Nodes.Select(x => (x.Index - 10000).ToString()).OrderBy(x => x));
                    Assert.AreEqual("0", actual);

                    query = $"+Name:'{nameBase}0' Index:10001 Index:10002";
                    queryResult = CreateSafeContentQuery(query).Execute();
                    actual = string.Join(", ",
                        queryResult.Nodes.Select(x => (x.Index - 10000).ToString()).OrderBy(x => x));
                    Assert.AreEqual("0", actual);

                    query = $"Name:'{nameBase}0' Index:10001 Index:10002";
                    queryResult = CreateSafeContentQuery(query).Execute();
                    actual = string.Join(", ",
                        queryResult.Nodes.Select(x => (x.Index - 10000).ToString()).OrderBy(x => x));
                    Assert.AreEqual("0, 1, 2", actual);
                }
            });
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Query_MustShouldNot_Fulltext()
        {
            await L29Test(async () =>
            {
                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    var root = Repository.Root;
                    var nameBase = "L29_Query_MustAndShould_Fulltext_";
                    var descriptions = new[]
                    {
                        "lucene",               // 0
                        "dotnet",               // 1
                        "jakarta",              // 2
                        "lucene jakarta",       // 3
                        "lucene dotnet",        // 4
                        "dotnet jakarta",       // 5
                        "lucene dotnet jakarta" // 6
                    };
                    var i = 0;
                    foreach (var description in descriptions)
                    {
                        var node = new SystemFolder(root) {Name = $"{nameBase}{i++}", Description = description};
                        await node.SaveAsync(CancellationToken.None);
                    }

                    string GetResult(string query)
                    {
                        var queryResult = CreateSafeContentQuery(query).Execute();
                        return string.Join(", ",
                            queryResult.Nodes.Select(x => x.Name.Last().ToString()).OrderBy(x => x));
                    }

                    Assert.AreEqual("0, 3, 4, 6", GetResult("+lucene"));
                    Assert.AreEqual("0, 3, 4, 6", GetResult("+lucene jakarta"));
                    Assert.AreEqual("0, 3, 4, 6", GetResult("+lucene dotnet jakarta"));
                    Assert.AreEqual("0, 1, 2, 3, 4, 5, 6", GetResult("lucene dotnet jakarta"));

                    Assert.AreEqual("0, 3, 4, 6", GetResult("+lucene"));
                    Assert.AreEqual("0, 4", GetResult("+lucene -jakarta"));
                    Assert.AreEqual("0", GetResult("lucene -dotnet -jakarta"));

                    Assert.AreEqual("4, 6", GetResult("+lucene +dotnet"));
                    Assert.AreEqual("4", GetResult("+lucene +dotnet -jakarta"));

                    Assert.AreEqual("0, 1, 3, 4, 5, 6", GetResult("lucene dotnet"));
                    Assert.AreEqual("0, 1, 4", GetResult("lucene dotnet -jakarta"));
                }
            });
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Query_Specialities()
        {
            await L29Test(async () =>
            {
                #region infrastructure
                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    var root = Repository.Root;
                    var nameBase = "L29_Query_Specialities_";
                    var data = new[]
                    {
                        new {id = 0, desc = "a0 b0 c0 d0"},
                        new {id = 1, desc = "a0 b0 c0 d1"},
                        new {id = 2, desc = "a0 b0 c1 d0"},
                        new {id = 3, desc = "a0 b0 c1 d1"},
                        new {id = 4, desc = "a0 b1 c0 d0"},
                        new {id = 5, desc = "a0 b1 c0 d1"},
                        new {id = 6, desc = "a0 b1 c1 d0"},
                        new {id = 7, desc = "a0 b1 c1 d1"},
                        new {id = 8, desc = "a1 b0 c0 d0"},
                        new {id = 9, desc = "a1 b0 c0 d1"},
                        new {id = 10, desc = "a1 b0 c1 d0"},
                        new {id = 11, desc = "a1 b0 c1 d1"},
                        new {id = 12, desc = "a1 b1 c0 d0"},
                        new {id = 13, desc = "a1 b1 c0 d1"},
                        new {id = 14, desc = "a1 b1 c1 d0"},
                        new {id = 15, desc = "a1 b1 c1 d1"},
                    };

                    for (var i = 0; i < data.Length; i++)
                    {
                        //GetData(data[i].desc, out var recordData, out var combinationData);
                        var content = Content.CreateNew("SystemFolder", root, $"{nameBase}{i}");
                        content["Description"] = data[i].desc;
                        //content["ExtensionData"] = combinationData;
                        content.Index = data[i].id;
                        await content.SaveAsync(CancellationToken.None);
                    }

                    string GetResult(string query)
                    {
                        var queryResult = CreateSafeContentQuery(query).Execute();
                        return string.Join(", ",
                            queryResult.Nodes.Select(x => x.Index).OrderBy(x => x).Select(x => x.ToString()));
                    }

                    #endregion

                    var d = "Description";
                    //var e = "ExtensionData";

                    // One SHOULD is MUST 
                    Assert.AreEqual("0, 1, 2, 3, 4, 5, 6, 7", GetResult($"+{d}:a0"));
                    Assert.AreEqual(GetResult($"+{d}:a0"), GetResult($"{d}:a0"));

                    // One SHOULD sub level is MUST
                    Assert.AreEqual("0, 1, 2, 3", GetResult($"+{d}:a0 +{d}:b0"));
                    Assert.AreEqual(GetResult($"+{d}:a0 +{d}:b0"), GetResult($"+{d}:a0 +({d}:b0)"));

                    // One SHOULD is MUST even if there are any MUST NOT
                    Assert.AreEqual(GetResult($"+{d}:a0 -{d}:b0"), GetResult($"{d}:a0 -{d}:b0")); // result: 4, 5, 6, 7
                    Assert.AreEqual(GetResult($"+{d}:a0 -{d}:b0 -{d}:c0"),
                        GetResult($"{d}:a0 -{d}:b0 -{d}:c0")); // result: 6, 7

                    // SHOULD is irrelevant if there is MUST.
                    Assert.AreEqual(GetResult($"+{d}:a0"),
                        GetResult($"+{d}:a0 {d}:b0 {d}:c0")); // result: 0, 1, 2, 3, 4, 5, 6, 7
                    Assert.AreEqual(GetResult($"+{d}:a0 +{d}:b0"),
                        GetResult($"+{d}:a0 +{d}:b0 {d}:c0")); // result: 0, 1, 2, 3

                    // SHOULD sub level is irrelevant next to a MUST.
                    Assert.AreEqual(GetResult($"+{d}:a0 ( {d}:b0  {d}:c0)"), GetResult($"+{d}:a0"));
                    Assert.AreEqual(GetResult($"+{d}:a0 (+{d}:b0 +{d}:c0)"), GetResult($"+{d}:a0"));

                    // SHOULD is irrelevant if there is MUST even if there are any MUST NOT
                    Assert.AreEqual(GetResult($"+{d}:a0 -{d}:c0 -{d}:d0"),
                        GetResult($"+{d}:a0 {d}:b0 -{d}:c0 -{d}:d0")); // result: 3, 7

                    // SHOULD and MUST terms in one level cannot be separated to sub level with parentheses
                    Assert.AreNotEqual(GetResult($"+{d}:a0 {d}:b0 {d}:c0"), GetResult($"+{d}:a0 +({d}:b0 {d}:c0)"));
                    Assert.AreEqual(GetResult($"+{d}:a0 {d}:b0 {d}:c0"), GetResult($"+{d}:a0"));

                    // One SHOULD sub level is MUST even if there are any MUST NOT
                    Assert.AreEqual(GetResult($"({d}:a0 {d}:b0) -{d}:c0"),
                        GetResult($"+({d}:a0 {d}:b0) -{d}:c0")); // result: 2, 3, 6, 7, 10, 11


                    // MORE INTERESTING QUERIES
                    // +a +b -c
                    Assert.AreEqual("2, 3", GetResult($"+{d}:a0 +{d}:b0 -{d}:c0"));
                    // a b -c
                    Assert.AreEqual("2, 3, 6, 7, 10, 11", GetResult($" {d}:a0  {d}:b0 -{d}:c0"));
                    // +a +b -c -d
                    Assert.AreEqual("3", GetResult($"+{d}:a0 +{d}:b0 -{d}:c0 -{d}:d0"));
                    //  a  b -c -d
                    Assert.AreEqual("3, 7, 11", GetResult($" {d}:a0  {d}:b0 -{d}:c0 -{d}:d0"));
                }
            });
        }

        [TestMethod, TestCategory("IR, L29")]
        public async Task L29_Query_Combinations()
        {
            await L29Test(async () =>
            {
                #region infrastructure

                var indexPopulator = Providers.Instance.SearchManager.GetIndexPopulator();

                using (new SystemAccount())
                {
                    var nameBase = "L29_Query_Combinations_";
                    var data = new[]
                    {
                        new {id = 0, desc = "a0 b0 c0 d0"},
                        new {id = 1, desc = "a0 b0 c0 d1"},
                        new {id = 2, desc = "a0 b0 c1 d0"},
                        new {id = 3, desc = "a0 b0 c1 d1"},
                        new {id = 4, desc = "a0 b1 c0 d0"},
                        new {id = 5, desc = "a0 b1 c0 d1"},
                        new {id = 6, desc = "a0 b1 c1 d0"},
                        new {id = 7, desc = "a0 b1 c1 d1"},
                        new {id = 8, desc = "a1 b0 c0 d0"},
                        new {id = 9, desc = "a1 b0 c0 d1"},
                        new {id = 10, desc = "a1 b0 c1 d0"},
                        new {id = 11, desc = "a1 b0 c1 d1"},
                        new {id = 12, desc = "a1 b1 c0 d0"},
                        new {id = 13, desc = "a1 b1 c0 d1"},
                        new {id = 14, desc = "a1 b1 c1 d0"},
                        new {id = 15, desc = "a1 b1 c1 d1"},
                    };

                    for (var i = 0; i < data.Length; i++)
                    {
                        GetCombinationData(data[i].desc, out var recordData, out var combinationData);
                        var content = Content.CreateNew("SystemFolder", Repository.Root, $"{nameBase}{i}");
                        content["Description"] = recordData;
                        content["ExtensionData"] = combinationData;
                        content.Index = data[i].id;
                        await content.SaveAsync(CancellationToken.None);
                    }

                    string GetResult(string query)
                    {
                        var queryResult = CreateSafeContentQuery(query).Execute();
                        return string.Join(", ",
                            queryResult.Nodes.Select(x => x.Index).OrderBy(x => x).Select(x => x.ToString()));
                    }

                    #endregion

                    var d = "Description";
                    var e = "ExtensionData";

                    // +a +b  -->  +ab
                    Assert.AreEqual(GetResult($"+{d}:a0 +{d}:b0"), GetResult($"+{e}:a0b0")); // result: 0, 1, 2, 3

                    // +a +b +c  -->  +abc
                    Assert.AreEqual(GetResult($"+{d}:a0 +{d}:b0 +{d}:c0"), GetResult($"+{e}:a0b0c0")); // result:  0, 1

                    // +a +(b c)  -->  ab ac
                    Assert.AreEqual(GetResult($"+{d}:a0 +({d}:b0 {d}:c0)"),
                        GetResult($"{e}:a0b0 {e}:a0c0")); // result: 0, 1, 2, 3, 4, 5

                    // (+a +b) (+c +d)  -->  ab cd
                    Assert.AreEqual(GetResult($"(+{d}:a0 +{d}:b0) (+{d}:c0 +{d}:d0)"),
                        GetResult($"{e}:a0b0 {e}:c0d0")); // result: 0, 1, 2, 3, 4, 8, 12
                }
            });
        }
        private void GetCombinationData(string inputRecord, out string recordData, out string combinationData)
        {
            var fields = inputRecord.Split(' ');
            var records = new[]
            {
                fields,
                new[] {"a0", "b9", "c9", "d9"},
                new[] {"a9", "b0", "c9", "d9"},
                new[] {"a9", "b9", "c0", "d9"},
                new[] {"a9", "b9", "c9", "d0"},
                new[] {"a1", "b9", "c9", "d9"},
                new[] {"a9", "b1", "c9", "d9"},
                new[] {"a9", "b9", "c1", "d9"},
                new[] {"a9", "b9", "c9", "d1"},
            };

            var combinations = new string[records.Length];
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                var a = record[0];
                var b = record[1];
                var c = record[2];
                var d = record[3];
                combinations[i] = $"{a} {b} {c} {d} {a}{b} {a}{c} {a}{d} {b}{c} {b}{d} {c}{d} {a}{b}{c} {a}{b}{d} {a}{c}{d} {b}{c}{d} {a}{b}{c}{d}";
            }

            recordData = inputRecord; // string.Join(" | ", records.Select(x => string.Join(" ", x)).ToArray());
            combinationData = string.Join(" | ", combinations.Select(x => string.Join(" ", x)).ToArray());
        }

        /* ======================================================================================= */

        protected override RepositoryBuilder CreateRepositoryBuilderForTest(Action<IServiceCollection> modifyServices = null)
        {
            var repoBuilder = base.CreateRepositoryBuilderForTest();

            repoBuilder.Console = new StringWriter();

            return repoBuilder
                .UseTraceCategories("ContentOperation", "Event", "Repository", "IndexQueue", "Index", "Query") as RepositoryBuilder;
        }

        private void GetAllIdValuesFromIndex(out int[] nodeIds, out int[] versionIds)
        {
            var nodeIdList = new List<int>();
            var versionIdLists = new List<int>();
            using (var rf = Lucene29LocalIndexingEngine.GetReaderFrame())
            {
                var reader = rf.IndexReader;
                for (var d = 0; d < reader.NumDocs(); d++)
                {
                    var doc = reader.Document(d);

                    var nodeIdString = doc.Get(IndexFieldName.NodeId);
                    if (!string.IsNullOrEmpty(nodeIdString))
                        nodeIdList.Add(int.Parse(nodeIdString));

                    var versionIdString = doc.Get(IndexFieldName.VersionId);
                    if (!string.IsNullOrEmpty(versionIdString))
                        versionIdLists.Add(int.Parse(versionIdString));
                }
            }
            nodeIds = nodeIdList.ToArray();
            versionIds = versionIdLists.ToArray();
        }

        public void EnsureEmptyIndexDirectory()
        {
            var path = Providers.Instance.SearchManager.IndexDirectoryPath;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            //IndexDirectory.Instance.CreateNew();
            //IndexManager.ClearIndex();
        }

        public static void DeleteIndexDirectories()
        {
            var path = Providers.Instance.SearchManager.IndexDirectoryPath;
            foreach (var indexDir in Directory.GetDirectories(path))
            {
                try
                {
                    Directory.Delete(indexDir, true);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
            foreach (var file in Directory.GetFiles(path))
            {
                try
                {
                    System.IO.File.Delete(file);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
    }
}
