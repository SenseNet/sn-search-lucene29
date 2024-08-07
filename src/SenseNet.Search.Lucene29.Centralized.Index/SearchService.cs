﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Search.Querying;
using SenseNet.Security;
using SenseNet.Security.Configuration;
using SenseNet.Security.Messaging;
using EventId = SenseNet.Diagnostics.EventId;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    // Re-created per request.
    public class SearchService : ISearchServiceContract
    {
        private static SecuritySystem _securitySystem;

        public static void Start(
            ISecurityDataProvider securityDataProvider,
            IMessageProvider messageProvider,
            ISecurityMessageFormatter messageFormatter,
            IMissingEntityHandler missingEntityHandler,
            MessagingOptions messagingOptions,
            string indexDirectoryPath,
            ILogger<SecuritySystem> logger,
            ILogger<SearchService> searchServiceLogger)
        {
            UpdateTraceCategories(searchServiceLogger);

            // Set index directory before touching the SearchManager class, so that the value
            // comes from the application above instead of an automatism in this library.
            if (!string.IsNullOrEmpty(indexDirectoryPath))
                SenseNet.Configuration.Indexing.IndexDirectoryFullPath = indexDirectoryPath;

            _securitySystem = SecurityHandler.StartSecurity(securityDataProvider, messageProvider, 
                messageFormatter, missingEntityHandler, messagingOptions, logger);

            using (var traceWriter = new TraceTextWriter())
            {
                SearchManager.Instance.Start(traceWriter);
                LoadIndexingInfo();
            }
        }

        public static void ShutDown()
        {
            _securitySystem.Shutdown();
            SearchManager.Instance.ShutDown();
        }

        #region ISearchServiceContract implementation

        public bool Alive()
        {
            // This method always returns true, because it is used only to determine
            // whether the service is accessible or not.
            return true;
        }

        public void ClearIndex()
        {
            SearchManager.Instance.ClearIndex();
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            return SearchManager.Instance.ReadActivityStatusFromIndex();
        }

        public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
        {
            return ExecuteQueryAsync(query, queryContext, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
        {
            var filter = GetPermissionFilter(query, queryContext);
            var lucQuery = Compile(query, null);
            var lucQueryResult = lucQuery.Execute(filter, null);
            var hits = lucQueryResult?.Select(x => x.NodeId).ToArray() ?? new int[0];

            return Task.FromResult(new QueryResult<int>(hits, lucQuery.TotalCount));
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            return ExecuteQueryAndProjectAsync(query, queryContext, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
        {
            var filter = GetPermissionFilter(query, queryContext);
            var lucQuery = Compile(query, null);
            var projection = query.Projection ?? IndexFieldName.NodeId;
            var lucQueryResult = lucQuery.Execute(filter, null);
            var hits = lucQueryResult?
                           .Select(x => x[projection, false])
                           .Where(r => !string.IsNullOrEmpty(r))
                           .ToArray()
                       ?? new string[0];

            return Task.FromResult(new QueryResult<string>(hits, lucQuery.TotalCount));
        }

        private static IPermissionFilter GetPermissionFilter(SnQuery query, ServiceQueryContext queryContext)
        {
            var security = new SecurityHandler(new ServiceSecurityContext(new SearchUser
            {
                Id = queryContext.UserId,
                DynamicGroups = queryContext.DynamicGroups
            }, _securitySystem));

            if (!Enum.TryParse(queryContext.FieldLevel, true, out QueryFieldLevel queryFieldLevel))
                queryFieldLevel = QueryFieldLevel.HeadOnly;

            return new ServicePermissionFilter(security, queryFieldLevel, query.AllVersions);
        }

        public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
        {
            using (var op = SnTrace.Index.StartOperation(
                $"WriteIndex: deletions:{deletions?.Length} updates:{updates?.Length} additions:{additions?.Length}"))
            {
                SearchManager.Instance.WriteIndex(deletions, updates, additions);
                op.Successful = true;
            }
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            SearchManager.Instance.WriteActivityStatusToIndex(state);
        }

        private static IBackupManagerFactory _backupManagerFactory { get; set; } = new BackupManager();
        private static readonly object _backupLock = new object();
        private static IBackupManager _backupManager;
        private static CancellationTokenSource _backupCancellationSource;
        private static readonly List<BackupInfo> _backupHistory = new List<BackupInfo>();
        public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            if (backupDirectoryPath == null)
            {
                SnTrace.Index.WriteError("SearchService: Missing 'backupDirectoryPath'");
                throw new ArgumentNullException(nameof(backupDirectoryPath));
            }

            if (_backupManager != null)
            {
                SnTrace.Index.Write("SearchService: Backup already executing by another thread.");
                return CreateBackupResponse(BackupState.Executing, false);
            }

            lock (_backupLock)
            {
                if (_backupManager != null)
                    return CreateBackupResponse(BackupState.Executing, false);
                _backupManager = _backupManagerFactory.CreateBackupManager();
            }

            SnTrace.Index.Write("SearchService: BackupManager created.");
            Task.Run(() => BackupWorker(state, backupDirectoryPath));

            return CreateBackupResponse(BackupState.Started, false);
        }

        private void BackupWorker(IndexingActivityStatus state, string backupDirectoryPath)
        {
            try
            {
                _backupCancellationSource = new CancellationTokenSource();
                _backupManager.Backup(state, backupDirectoryPath, SearchManager.Instance,
                    _backupCancellationSource.Token);
            }
            catch (Exception e)
            {
                CollectErrorMessages(e, _backupManager.BackupInfo);
                SnTrace.Index.WriteError("SearchService: " + _backupManager.BackupInfo.Message);
            }

            _backupHistory.Add(_backupManager.BackupInfo.Clone());
            SnTrace.Index.Write("SearchService: BackupInfo is added to history.");
            _backupManager = null;
            _backupCancellationSource.Dispose();
            _backupCancellationSource = null;
        }

        private void CollectErrorMessages(Exception exception, BackupInfo targetInfo)
        {
            var sb = new StringBuilder(exception is TaskCanceledException ? "CANCELED: " : "ERROR: ");
            CollectErrorMessages(exception, sb, "");
            targetInfo.Message = sb.ToString();
        }
        private void CollectErrorMessages(Exception exception, StringBuilder sb, string indent)
        {
            sb.Append(indent);
            sb.Append(exception.GetType().FullName).Append(": ");
            sb.AppendLine(exception.Message);
            if (exception is AggregateException ae)
            {
                var indent2 = indent + "  ";
                foreach (var ex in ae.InnerExceptions)
                    CollectErrorMessages(ex, sb, indent2);
            }
            if (exception.InnerException != null)
                CollectErrorMessages(exception.InnerException, sb, indent + "  ");
        }

        public BackupResponse QueryBackup()
        {
            BackupState state;
            if (_backupManager != null)
            {
                state = BackupState.Executing;
            }
            else
            {
                BackupInfo info = _backupHistory.FirstOrDefault();
                if (info == null)
                {
                    state = BackupState.Initial;
                }
                else
                {
                    if (info.Message != null)
                    {
                        state = info.Message.StartsWith("Cancel", StringComparison.OrdinalIgnoreCase)
                            ? BackupState.Canceled
                            : BackupState.Faulted;
                    }
                    else
                    {
                        state = BackupState.Finished;
                    }
                }
            }

            return CreateBackupResponse(state, true);
        }
        public BackupResponse CancelBackup()
        {
            _backupCancellationSource?.Cancel();
            return CreateBackupResponse(BackupState.CancelRequested, true);
        }
        private BackupResponse CreateBackupResponse(BackupState state, bool withHistory)
        {
            return new BackupResponse
            {
                State = state,
                Current = _backupManager?.BackupInfo.Clone(),
                History = withHistory ? _backupHistory.ToArray() : null,
            };
        }

        private static readonly object SaveIndexingInfoLock = new object();
        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, 
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames)
        {
            lock (SaveIndexingInfoLock)
            {
                var analyzers = analyzerTypes
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => GetAnalyzer(kvp.Value));

                SnTrace.Index.Write("Indexing info set.");
                SaveIndexingInfo(analyzerTypes, indexFieldTypes, sortFieldNames);

                SearchManager.Instance.SetIndexingInfo(analyzers, indexFieldTypes);
                SearchManager.SortFieldNames = sortFieldNames;
            }
        }

        private static readonly string IndexingInfoRootPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexingInfo");
        private static readonly string AnalyzersPath = Path.Combine(IndexingInfoRootPath, "Analyzers");
        private static readonly string FieldTypesPath = Path.Combine(IndexingInfoRootPath, "IndexFieldTypes");
        private static readonly string SortNamesPath = Path.Combine(IndexingInfoRootPath, "SortFieldNames");
        private void SaveIndexingInfo(
            IDictionary<string, IndexFieldAnalyzer> analyzerTypes,
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames)
        {
            try
            {
                if (!Directory.Exists(IndexingInfoRootPath))
                    Directory.CreateDirectory(IndexingInfoRootPath);

                using (var writer = new StreamWriter(AnalyzersPath))
                    writer.Write(JsonConvert.SerializeObject(analyzerTypes, Formatting.Indented));
                using (var writer = new StreamWriter(FieldTypesPath))
                    writer.Write(JsonConvert.SerializeObject(indexFieldTypes, Formatting.Indented));
                using (var writer = new StreamWriter(SortNamesPath))
                   writer.Write(JsonConvert.SerializeObject(sortFieldNames, Formatting.Indented));

                SnTrace.Index.Write("Indexing info is saved to App_Data/IndexingInfo");
            }
            catch (Exception ex)
            {
                SnTrace.Index.WriteError("Cannot persist indexing info: " + ex.Message);
            }

        }
        private static void LoadIndexingInfo()
        {
            var analyzers = LoadIndexingInfoFile<Dictionary<string, IndexFieldAnalyzer>>(AnalyzersPath)?
                .ToDictionary(kvp => kvp.Key, kvp => GetAnalyzer(kvp.Value));
            var indexFieldTypes = LoadIndexingInfoFile<Dictionary<string, IndexValueType>>(FieldTypesPath);
            var sortFieldNames = LoadIndexingInfoFile<Dictionary<string, string>>(SortNamesPath);

            if (analyzers == null || indexFieldTypes == null || sortFieldNames == null)
            {
                SnTrace.Index.Write("Indexing info cannot be loaded from App_Data/IndexingInfo because " +
                                    "a file is missing or incomplete.");
                return;
            }

            SnTrace.Index.Write($"Indexing info is loaded from App_Data/IndexingInfo");

            SearchManager.Instance.SetIndexingInfo(analyzers, indexFieldTypes);
            SearchManager.SortFieldNames = sortFieldNames;
        }
        private static T LoadIndexingInfoFile<T>(string path) where T : class
        {
            if (!File.Exists(path))
                return null;

            using (var reader = new StreamReader(path))
            {
                var fileText = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<T>(fileText);
            }
        }

        public IndexProperties GetIndexProperties()
        {
            return new IndexExplorer(SearchManager.Instance).GetIndexProperties();
        }
        public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
        {
            return new IndexExplorer(SearchManager.Instance).GetInvertedIndexAsync(fieldName, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }
        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            return new IndexExplorer(SearchManager.Instance).GetIndexDocumentByVersionId(versionId);
        }
        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            return new IndexExplorer(SearchManager.Instance).GetIndexDocumentByDocumentId(documentId);
        }

        //===================================================================================== Health methods

        public IDictionary<string, string> GetConfigurationInfo()
        {
            var searchManager = SearchManager.Instance;
            return new Dictionary<string, string>
            {
                {"IndexIsCentralized", "true"},
                {"SearchService_IndexDirectory", searchManager.IndexDirectory.CurrentDirectory},
                {"SearchService_Security_ConnectionString", _securitySystem.DataProvider.ConnectionString},
                {"SearchService_Security_Messaging_ComputerId", _securitySystem.MessageSenderManager.ComputerId},
                {"SearchService_Security_Messaging_InstanceId", _securitySystem.MessageSenderManager.InstanceId},
                {"SearchService_Security_Messaging_ReceiverName", _securitySystem.MessageProvider.ReceiverName},
            };
        }

        public IDictionary<string, string> GetHealth()
        {
            throw new NotImplementedException();
        }

        #endregion

        //===================================================================================== Helper methods

        internal static Analyzer GetAnalyzer(IndexFieldAnalyzer analyzerToken)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (analyzerToken)
            {
                case IndexFieldAnalyzer.Keyword: return new KeywordAnalyzer();
                case IndexFieldAnalyzer.Standard: return new StandardAnalyzer(LuceneSearchManager.LuceneVersion);
                case IndexFieldAnalyzer.Whitespace: return new WhitespaceAnalyzer();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static LucQuery Compile(SnQuery query, IQueryContext context)
        {
            var analyzer = SearchManager.Instance.GetAnalyzer();
            var visitor = new SnQueryToLucQueryVisitor(analyzer, context);
            visitor.Visit(query.QueryTree);

            var result = LucQuery.Create(visitor.Result, SearchManager.Instance);
            result.Skip = query.Skip;
            result.Top = query.Top;
            result.SortFields = query.Sort?.Select(s => CreateSortField(s.FieldName, s.Reverse)).ToArray() ?? new SortField[0];
            result.EnableAutofilters = query.EnableAutofilters;
            result.EnableLifespanFilter = query.EnableLifespanFilter;
            result.QueryExecutionMode = query.QueryExecutionMode;
            result.CountOnly = query.CountOnly;
            result.CountAllPages = query.CountAllPages;

            return result;
        }
        private static SortField CreateSortField(string fieldName, bool reverse)
        {
            if (string.IsNullOrEmpty(fieldName))
                throw new ArgumentException(nameof(fieldName));

            int sortType;
            var fieldType = default(IndexValueType);

            // ReSharper disable once AssignNullToNotNullAttribute
            SearchManager.Instance?.IndexFieldTypeInfo?.TryGetValue(fieldName, out fieldType);

            switch (fieldType)
            {
                case IndexValueType.Bool:
                case IndexValueType.String:
                case IndexValueType.StringArray:
                    sortType = SortField.STRING;
                    break;
                case IndexValueType.IntArray:
                case IndexValueType.Int:
                    sortType = SortField.INT;
                    break;
                case IndexValueType.DateTime:
                case IndexValueType.Long:
                    sortType = SortField.LONG;
                    break;
                case IndexValueType.Float:
                    sortType = SortField.FLOAT;
                    break;
                case IndexValueType.Double:
                    sortType = SortField.DOUBLE;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var sfn = fieldName;
            if (SearchManager.SortFieldNames?.TryGetValue(fieldName, out sfn) ?? false)
                fieldName = sfn;

            return sortType == SortField.STRING
                ? new SortField(fieldName, System.Threading.Thread.CurrentThread.CurrentCulture, reverse)
                : new SortField(fieldName, sortType, reverse);
        }

        private static void UpdateTraceCategories(ILogger logger)
        {
            foreach (var category in SnTrace.Categories.Where(c => !c.Enabled))
                category.Enabled = Tracing.TraceCategories.Contains(category.Name);

            var categories = string.Join(", ", SnTrace.Categories.Select(category => $"{category.Name}: {category.Enabled}"));

            logger.LogInformation("Trace settings were updated in Search service. {TraceCategories}", categories);
        }

        public static void InitializeForTest(IBackupManagerFactory factoryForTest)
        {
            _backupCancellationSource?.Cancel();
            while(_backupManager!=null)
                Thread.Sleep(100);
            _backupHistory.Clear();
            _backupManagerFactory = factoryForTest;
        }
    }
}
