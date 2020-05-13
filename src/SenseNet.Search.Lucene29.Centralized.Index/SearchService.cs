using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public class SearchService : ISearchServiceContract
    {
        public static void Start()
        {
            UpdateTraceCategories();

            SecurityHandler.StartSecurity();

            using (var traceWriter = new TraceTextWriter())
            {
                SearchManager.Instance.Start(traceWriter);
            }
        }

        public static void ShutDown()
        {
            SearchManager.Instance.ShutDown();
        }

        #region ISearchServiceContract implementation

        public bool Alive()
        {
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
            var filter = GetPermissionFilter(query, queryContext);
            var lucQuery = Compile(query, null);
            var lucQueryResult = lucQuery.Execute(filter, null);
            var hits = lucQueryResult?.Select(x => x.NodeId).ToArray() ?? new int[0];

            return new QueryResult<int>(hits, lucQuery.TotalCount);
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
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

            return new QueryResult<string>(hits, lucQuery.TotalCount);
        }

        private static IPermissionFilter GetPermissionFilter(SnQuery query, ServiceQueryContext queryContext)
        {
            var security = new SecurityHandler(new ServiceSecurityContext(new SearchUser
            {
                Id = queryContext.UserId,
                DynamicGroups = queryContext.DynamicGroups
            }));

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

        internal IBackupManagerFactory BackupManagerFactory { get; set; } = new BackupManager();
        private readonly object _backupLock = new object();
        private IBackupManager _backupManager;
        public IndexBackupResult Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            if (_backupManager != null)
                return IndexBackupResult.AlreadyExecuting;

            lock (_backupLock)
            {
                if (_backupManager != null)
                    return IndexBackupResult.AlreadyExecuting;
                _backupManager = BackupManagerFactory.CreateBackupManager();
            }

            _backupManager.BackupAsync(state, backupDirectoryPath, SearchManager.Instance, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            _backupManager = null;

            return IndexBackupResult.Finished;
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, 
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames)
        {
            var analyzers = analyzerTypes.ToDictionary(kvp => kvp.Key, kvp => GetAnalyzer(kvp.Value));

            SnTrace.Index.Write("Indexing info set.");

            SearchManager.Instance.SetIndexingInfo(analyzers, indexFieldTypes);
            SearchManager.SortFieldNames = sortFieldNames;
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

        private static void UpdateTraceCategories()
        {
            foreach (var category in SnTrace.Categories)
                category.Enabled = Tracing.TraceCategories.Contains(category.Name);

            SnLog.WriteInformation("Trace settings were updated in Search service.", EventId.NotDefined,
                properties: SnTrace.Categories.ToDictionary(c => c.Name, c => (object)c.Enabled.ToString()));
        }
    }
}
