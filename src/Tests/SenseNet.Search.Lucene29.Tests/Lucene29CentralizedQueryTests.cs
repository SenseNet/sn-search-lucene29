using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;
using SenseNet.Testing;
using SenseNet.Tests.Core;

namespace SenseNet.Search.Lucene29.Tests
{
    internal interface IMock
    {
        void Setup<T>(MethodBase method, object callbacks);
        int Calls { get; }
    }

    internal static class MockExtensions
    {
        public static IMock CurrentMock { get; set; }
        public static MethodBase CurrentMethod { get; set; }

        public static object Calls<T>(this T value, Func<T> callback, params Func<T>[] moreCallbacks)
        {
            var callbacks = new List<Func<T>> { callback };
            callbacks.AddRange(moreCallbacks);

            CurrentMock.Setup<T>(CurrentMethod, callbacks.ToArray());

            return value;
        }

        public static T HandleMethod<T>(IMock mock, MethodBase currentMethod, Dictionary<MethodBase, int> callCountStore,
            Dictionary<MethodBase, object> callbackStore)
        {
            if (callbackStore.TryGetValue(currentMethod, out var value))
            {
                var callbacks = (Func<T>[])value;
                var calls = callCountStore[currentMethod];
                callCountStore[currentMethod] = calls + 1;
                return callbacks[calls]();
            }

            MockExtensions.CurrentMock = mock;
            MockExtensions.CurrentMethod = currentMethod;
            return default(T);
        }
    }

    [TestClass]
    public class Lucene29CentralizedQueryTests : TestBase
    {
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

        private class TestServiceClient : ISearchServiceClient, IMock
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
            public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
            {
                throw new NotImplementedException();
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

            public IDictionary<string, string> GetConfigurationInfo()
            {
                throw new NotImplementedException();
            }
            public IDictionary<string, string> GetHealth()
            {
                throw new NotImplementedException();
            }

            public void Start()
            {
                throw new NotImplementedException();
            }
            public void ShutDown()
            {
                throw new NotImplementedException();
            }

            public ISearchServiceClient CreateInstance() => this;

            private Dictionary<MethodBase, int> _callCounts = new Dictionary<MethodBase, int>();
            private Dictionary<MethodBase, object> _callbacks = new Dictionary<MethodBase, object>();

            public Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
            {
                var currentMethod = MethodBase.GetCurrentMethod();
                return MockExtensions.HandleMethod<Task<QueryResult<int>>>(this, currentMethod, _callCounts, _callbacks);
            }
            public Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
            {
                var currentMethod = MethodBase.GetCurrentMethod();
                return MockExtensions.HandleMethod<Task<QueryResult<string>>>(this, currentMethod, _callCounts, _callbacks);
            }

            public int Calls => _callCounts.Values.Sum();
            public void Setup<T>(MethodBase method, object callbacks)
            {
                _callbacks[method] = callbacks;
                _callCounts[method] = 0;
            }
        }
        #endregion

        [TestMethod]
        public async Task L29_AsyncQuery_ExecuteAsync()
        {
            var testServiceClient = new TestServiceClient();
            SearchServiceClient.Instance = testServiceClient;

            testServiceClient.ExecuteQueryAsync(null, null, default).Calls(
                () => Task.FromResult(new QueryResult<int>(new[] { 1, 2, 3 }, 3))
            );

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
            Assert.AreEqual("1,2,3", string.Join(",", result.Hits.Select(x => x.ToString())));
            Assert.AreEqual(1, testServiceClient.Calls);
        }
        [TestMethod]
        public async Task L29_AsyncQuery_ExecuteAsync_Retry()
        {
            var testServiceClient = new TestServiceClient();
            SearchServiceClient.Instance = testServiceClient;

            testServiceClient.ExecuteQueryAsync(null, null, default).Calls(
                () => throw new Exception("## ExecuteQueryAsync error ##"),
                () => Task.FromResult(new QueryResult<int>(new[] { 1, 2, 4 }, 3))
            );

            var services = new ServiceCollection()
                .AddSingleton<ISearchManager, TestSearchManager>()
                .BuildServiceProvider();
            Providers.Instance = new Providers(services);

            var engine = new Lucene29CentralizedQueryEngine { ServiceQueryContextFactory = new TestServiceQueryContextFactory() };
            var userId = 1;
            var context = new SnQueryContext(QuerySettings.AdminSettings, userId);
            var query = SnQuery.Parse("Id:1", context);

            QueryResult<int> result = null;
            using (new Swindler<int>(1,
                       () => SearchServiceClient.RetryWaitMilliseconds,
                       value => SearchServiceClient.RetryWaitMilliseconds = value))
            {
                // ACTION
                result = await engine.ExecuteQueryAsync(query, new TestPermissionFilter(), context, CancellationToken.None);
            }

            // ASSERT
            Assert.AreEqual("1,2,4", string.Join(",", result.Hits.Select(x => x.ToString())));
            Assert.AreEqual(2, testServiceClient.Calls);
        }
        [TestMethod]
        public async Task L29_AsyncQuery_ExecuteAndProjectAsync()
        {
            var testServiceClient = new TestServiceClient();
            SearchServiceClient.Instance = testServiceClient;

            testServiceClient.ExecuteQueryAndProjectAsync(null, null, default).Calls(
                () => Task.FromResult(new QueryResult<string>(new[] { "one", "two", "three" }, 3))
            );

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
            Assert.AreEqual(1, testServiceClient.Calls);
        }
        [TestMethod]
        public async Task L29_AsyncQuery_ExecuteAndProjectAsync_Retry()
        {
            var testServiceClient = new TestServiceClient();
            SearchServiceClient.Instance = testServiceClient;

            testServiceClient.ExecuteQueryAndProjectAsync(null, null, default).Calls(
                () => throw new Exception("## ExecuteQueryAsync error ##"),
                () => throw new Exception("## ExecuteQueryAsync error ##"),
                () => Task.FromResult(new QueryResult<string>(new[] { "one", "two", "three" }, 3))
            );

            var services = new ServiceCollection()
                .AddSingleton<ISearchManager, TestSearchManager>()
                .BuildServiceProvider();
            Providers.Instance = new Providers(services);

            var engine = new Lucene29CentralizedQueryEngine { ServiceQueryContextFactory = new TestServiceQueryContextFactory() };
            var userId = 1;
            var context = new SnQueryContext(QuerySettings.AdminSettings, userId);
            var query = SnQuery.Parse("Name:whatever .SELECT:Name", context);

            QueryResult<string> result = null;
            using (new Swindler<int>(1,
                       () => SearchServiceClient.RetryWaitMilliseconds,
                       value => SearchServiceClient.RetryWaitMilliseconds = value))
            {
                // ACTION
                result = await engine.ExecuteQueryAndProjectAsync(query, new TestPermissionFilter(), context, CancellationToken.None);
            }

            // ASSERT
            Assert.AreEqual("one,two,three", string.Join(",", result.Hits));
            Assert.AreEqual(3, testServiceClient.Calls);
        }
    }
}
