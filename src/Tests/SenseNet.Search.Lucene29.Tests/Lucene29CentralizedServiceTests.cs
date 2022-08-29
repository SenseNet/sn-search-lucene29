using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Lucene29.Centralized.Index;
using SenseNet.Search.Querying;
using SenseNet.Tests.Core;
using SearchManager = SenseNet.Search.Lucene29.Centralized.Index.SearchManager;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29CentralizedServiceTests : TestBase
    {
        [TestMethod]
        public void L29_Service_Backup_OnlyOne()
        {
            SearchService.InitializeForTest(new BackupManager_for_OnlyOneTest());
            var service = new SearchService();

            var tasks = Enumerable.Range(0, 5)
                .Select(x => Task.Run(() => service.Backup(null, "fakeBackupDirectoryPath")))
                .ToArray();
            // ReSharper disable once CoVariantArrayConversion
            Task.WaitAll(tasks);

            var completed = new string(tasks
                .Select(t => (char) ('0' + (int) t.Result.State))
                .OrderBy(x => x)
                .ToArray());
            var faulted = new string(tasks
                .Select(t => t.IsFaulted ? 'Y' : 'n')
                .ToArray());

            // 1 = Started, 2 = AlreadyStarted,
            Assert.AreEqual("12222", completed);
            Assert.AreEqual("nnnnn", faulted);
        }

        #region private class BackupManager_for_OnlyOneTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_OnlyOneTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_OnlyOneTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();
            public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).GetAwaiter().GetResult();
            }
        }
        #endregion

        [TestMethod]
        public void L29_Service_Backup_Progress()
        {
            SearchService.InitializeForTest(new BackupManager_for_ProgressTest());
            var service = new SearchService();

            BackupResponse response;
            var responses = new List<BackupResponse>();

            responses.Add(response = service.Backup(null, "fakeBackupDirectoryPath"));
            var backupInfo = response.Current;
            Assert.IsNotNull(backupInfo);
            Assert.AreEqual(BackupState.Started, response.State);

            var timeout = TimeSpan.FromSeconds(5.0d);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                Thread.Sleep(400);
                responses.Add(response = service.QueryBackup());
                if (response.State != BackupState.Executing)
                    break;
            }

            var states = responses.Select(r => r.State).Distinct().ToArray();
            var bytes = responses.Select(r => (r.Current ?? r.History[0]).CopiedBytes).Distinct().ToArray();
            var files = responses.Select(r => (r.Current ?? r.History[0]).CopiedFiles).Distinct().ToArray();
            var names = responses.Select(r => (r.Current ?? r.History[0]).CurrentlyCopiedFile ?? "").Distinct().ToArray();

            AssertSequenceEqual(new[] { BackupState.Started, BackupState.Executing, BackupState.Finished }, states);
            AssertSequenceEqual(new long[] { 0, 42, 2 * 42, 3 * 42 }, bytes);
            AssertSequenceEqual(new [] { 0, 1, 2, 3 }, files);
            AssertSequenceEqual(new [] { "", "File1", "File2", "File3" }, names);
        }
        #region private class BackupManager_for_ProgressTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_ProgressTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_ProgressTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();
            /// <summary>
            /// RUNS NOT EXCLUSIVE. DO NOT CALL TWICE.
            /// </summary>
            public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                BackupInfo.StartedAt = DateTime.UtcNow;

                BackupInfo.TotalBytes = 3 * 42L;
                BackupInfo.CountOfFiles = 3;

                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                BackupInfo.CurrentlyCopiedFile = "File1";

                for (int i = 0; i < 3; i++)
                {
                    Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

                    BackupInfo.CopiedBytes += 42;
                    BackupInfo.CopiedFiles++;
                    BackupInfo.CurrentlyCopiedFile = i < 2 ? $"File{i + 2}" : null;
                }

                BackupInfo.FinishedAt = DateTime.UtcNow;
            }
        }
        #endregion

        [TestMethod]
        public void L29_Service_Backup_Cancellation()
        {
            SearchService.InitializeForTest(new BackupManager_for_CancellationTest());
            var service = new SearchService();

            BackupResponse response;
            var responses = new List<BackupResponse>();

            responses.Add(response = service.Backup(null, "fakeBackupDirectoryPath"));
            var backupInfo = response.Current;
            Assert.IsNotNull(backupInfo);
            Assert.AreEqual(BackupState.Started, response.State);

            var timeout = TimeSpan.FromSeconds(5.0d);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < timeout)
            {
                Thread.Sleep(400);
                responses.Add(response = service.QueryBackup());
                if (response.State != BackupState.Executing)
                    break;
            }
            Thread.Sleep(400);
            responses.Add(service.CancelBackup());
            Thread.Sleep(400);
            responses.Add(service.QueryBackup());

            var states = responses.Select(r => r.State).Distinct().ToArray();
            var bytes = responses.Select(r => (r.Current ?? r.History[0]).CopiedBytes).Distinct().ToArray();
            var files = responses.Select(r => (r.Current ?? r.History[0]).CopiedFiles).Distinct().ToArray();
            var names = responses.Select(r => (r.Current ?? r.History[0]).CurrentlyCopiedFile ?? "").Distinct().ToArray();
            var messages = responses.Select(r => (r.Current ?? r.History[0]).Message ?? "").Distinct().ToArray();

            var count = files.Length;
            var expectedStates = new[]
            {
                BackupState.Started, BackupState.Executing,
                BackupState.CancelRequested, BackupState.Canceled
            };
            var expectedFiles = Enumerable.Range(0, count).ToArray();
            var expectedBytes = expectedFiles.Select(x => x * 42L).ToArray();
            var expectedNames = (new[] {""}).Union(expectedFiles.Select(x => $"File{x + 1}")).ToArray();

            AssertSequenceEqual(expectedStates, states);
            AssertSequenceEqual(expectedBytes, bytes);
            AssertSequenceEqual(expectedFiles, files);
            AssertSequenceEqual(expectedNames, names);
        }
        #region private class BackupManager_for_CancellationTest
        // ReSharper disable once InconsistentNaming
        private class BackupManager_for_CancellationTest : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new BackupManager_for_CancellationTest();
            }
            public BackupInfo BackupInfo { get; } = new BackupInfo();

            /// <summary>
            /// RUNS NOT EXCLUSIVE. DO NOT CALL TWICE.
            /// </summary>
            public void Backup(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                BackupInfo.StartedAt = DateTime.UtcNow;

                BackupInfo.TotalBytes = 333333333L;
                BackupInfo.CountOfFiles = 33333;

                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                BackupInfo.CurrentlyCopiedFile = "File1";

                var i = 0;
                while (true)
                {
                    Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

                    BackupInfo.CopiedBytes += 42;
                    BackupInfo.CopiedFiles++;
                    BackupInfo.CurrentlyCopiedFile = $"File{++i + 1}";
                }
                // ReSharper disable once FunctionNeverReturns
            }
        }
        #endregion

        /* =================================================================== */

        #region Mocks
        private class TestSearchManager : ISearchManager
        {
            public bool IsAutofilterEnabled(FilterStatus value)
            {
                throw new NotImplementedException();
            }
            public bool IsLifespanFilterEnabled(FilterStatus value)
            {
                throw new NotImplementedException();
            }
            public QueryResult ExecuteContentQuery(string text, QuerySettings settings, params object[] parameters)
            {
                throw new NotImplementedException();
            }
            public IIndexPopulator GetIndexPopulator()
            {
                throw new NotImplementedException();
            }
            public IPerFieldIndexingInfo GetPerFieldIndexingInfo(string fieldName)
            {
                if (fieldName == "Id")
                    return new PerFieldIndexingInfo
                    {
                        Analyzer = IndexFieldAnalyzer.Keyword,
                        FieldDataType = typeof(int),
                        IndexFieldHandler = new IntegerIndexHandler(),
                        IndexStoringMode = IndexStoringMode.Default,
                        IndexingMode = IndexingMode.Default,
                        TermVectorStoringMode = IndexTermVector.Default
                    };
                if (fieldName == "Name")
                    return new PerFieldIndexingInfo
                    {
                        Analyzer = IndexFieldAnalyzer.Keyword,
                        FieldDataType = typeof(string),
                        IndexFieldHandler = new LowerStringIndexHandler(),
                        IndexStoringMode = IndexStoringMode.Default,
                        IndexingMode = IndexingMode.Default,
                        TermVectorStoringMode = IndexTermVector.Default
                    };
                throw new NotImplementedException();
            }
            public ISearchEngine SearchEngine { get; }
            public string IndexDirectoryPath { get; set; }
            public bool IsOuterEngineEnabled { get; set; }
            public FilterStatus EnableAutofiltersDefaultValue { get; }
            public FilterStatus EnableLifespanFilterDefaultValue { get; }
            public bool ContentQueryIsAllowed { get; }
        }

        private class TestPermissionFilter : IPermissionFilter
        {
            public bool IsPermitted(int nodeId, bool isLastPublic, bool isLastDraft)
            {
                return true;
            }
        }

        private class TestServiceClient : ISearchServiceClient
        {
            public bool Alive()
            {
                throw new NotImplementedException();
            }
            public void ClearIndex()
            {
                throw new NotImplementedException();
            }
            public IndexingActivityStatus ReadActivityStatusFromIndex()
            {
                throw new NotImplementedException();
            }
            public void WriteActivityStatusToIndex(IndexingActivityStatus state)
            {
                throw new NotImplementedException();
            }
            public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
            {
                throw new NotImplementedException();
            }
            public BackupResponse QueryBackup()
            {
                throw new NotImplementedException();
            }
            public BackupResponse CancelBackup()
            {
                throw new NotImplementedException();
            }
            public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
            {
                throw new NotImplementedException();
            }
            public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, IDictionary<string, IndexValueType> indexFieldTypes, IDictionary<string, string> sortFieldNames)
            {
                throw new NotImplementedException();
            }
            public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
            {
                throw new NotImplementedException();
            }
            internal int Calls = 0;
            public Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
            {
                if (++Calls < 3)
                    throw new Exception("## ExecuteQueryAsync error ##");
                return Task.FromResult(new QueryResult<int>(new[] {1, 2, 3}, 3));
            }
            public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
            {
                throw new NotImplementedException();
            }
            public Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
            {
                if (++Calls < 2)
                    throw new Exception("## ExecuteQueryAsync error ##");
                return Task.FromResult(new QueryResult<string>(new[] { "one", "two", "three" }, 3));
            }
            public IndexProperties GetIndexProperties()
            {
                throw new NotImplementedException();
            }
            public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
            {
                throw new NotImplementedException();
            }
            public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
            {
                throw new NotImplementedException();
            }
            public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
            {
                throw new NotImplementedException();
            }
            public ISearchServiceClient CreateInstance() => this;
            public void Start()
            {
                throw new NotImplementedException();
            }
            public void ShutDown()
            {
                throw new NotImplementedException();
            }
        }

        private class TestServiceQueryContextFactory : IServiceQueryContextFactory
        {
            public ServiceQueryContext Create(SnQuery query, IQueryContext context)
            {
                return new ServiceQueryContext
                {
                    UserId = context.UserId,
                    FieldLevel = PermissionFilter.GetFieldLevel(query).ToString(),
                    DynamicGroups = Array.Empty<int>()
                };
            }
        }
        #endregion
        [TestMethod]
        public async Task L29_AsyncQuery_ExecuteAsync()
        {
            var testServiceClient = new TestServiceClient();
            SearchServiceClient.Instance = testServiceClient;

            var services = new ServiceCollection()
                .AddSingleton<ISearchManager, TestSearchManager>()
                .BuildServiceProvider();
            Providers.Instance = new Providers(services);

            var engine = new Lucene29CentralizedQueryEngine { ServiceQueryContextFactory = new TestServiceQueryContextFactory() };
            var userId = 1;
            var context = new SnQueryContext(QuerySettings.AdminSettings, userId);
            var query = SnQuery.Parse("Id:1", context);

            // ACTION
            var result = await engine.ExecuteQueryAsync(query, new TestPermissionFilter(), context, CancellationToken.None);

            // ASSERT
            Assert.AreEqual("1,2,3", string.Join(",", result.Hits.Select(x=>x.ToString())));
            Assert.AreEqual(3, testServiceClient.Calls);
        }
        [TestMethod]
        public async Task L29_AsyncQuery_ExecuteAndProjectAsync()
        {
            var testServiceClient = new TestServiceClient();
            SearchServiceClient.Instance = testServiceClient;

            var services = new ServiceCollection()
                .AddSingleton<ISearchManager, TestSearchManager>()
                .BuildServiceProvider();
            Providers.Instance = new Providers(services);

            var engine = new Lucene29CentralizedQueryEngine { ServiceQueryContextFactory = new TestServiceQueryContextFactory() };
            var userId = 1;
            var context = new SnQueryContext(QuerySettings.AdminSettings, userId);
            var query = SnQuery.Parse("Name:whatever .SELECT:Name", context);

            // ACTION
            var result = await engine.ExecuteQueryAndProjectAsync(query, new TestPermissionFilter(), context, CancellationToken.None);

            // ASSERT
            Assert.AreEqual("one,two,three", string.Join(",", result.Hits));
            Assert.AreEqual(2, testServiceClient.Calls);
        }
    }
}
