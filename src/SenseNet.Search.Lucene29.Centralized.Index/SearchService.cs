using System;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public class SearchService : ISearchServiceContract
    {
        public static void Start()
        {
            //UNDONE: provide a console object
            SearchManager.Instance.Start(null);
        }

        public static void ShutDown()
        {
            //UNDONE: [INDEX] CommitState: maybe need to write the final state in the distributed environment.
            // IndexManager.GetCurrentIndexingActivityStatus()
            // WriteActivityStatusToIndex
            SearchManager.Instance.ShutDown();
        }

        #region ISearchServiceContract implementation

        public void ClearIndex()
        {
            SearchManager.Instance.ClearIndex();
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            return SearchManager.Instance.ReadActivityStatusFromIndex();
        }

        public QueryResult<int> ExecuteQuery(SnQuery query)
        {
            //UNDONE: [USER] determine the user id (always admin?)
            var lucQuery = Compile(query, null);

            //UNDONE: [QUERY] permission filter?
            var lucQueryResult = lucQuery.Execute(new AllowEverythingPermissionFilter(), null);
            var hits = lucQueryResult?.Select(x => x.NodeId).ToArray() ?? new int[0];

            return new QueryResult<int>(hits, lucQuery.TotalCount);
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query)
        {
            //UNDONE: [QUERY] parse and execute query with projection
            var lucQuery = Compile(query, null);
            var projection = query.Projection ?? IndexFieldName.NodeId;
            var lucQueryResult = lucQuery.Execute(new AllowEverythingPermissionFilter(), null);
            var hits = lucQueryResult?
                           .Select(x => x[projection, false])
                           .Where(r => !string.IsNullOrEmpty(r))
                           .ToArray()
                       ?? new string[0];

            return new QueryResult<string>(hits, lucQuery.TotalCount);
        }

        public void WriteIndex(IEnumerable<SnTerm> deletions, IEnumerable<DocumentUpdate> updates, IEnumerable<IndexDocument> additions)
        {
            SearchManager.Instance.WriteIndex(deletions, updates, additions);
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            SearchManager.Instance.WriteActivityStatusToIndex(state);
        }

        //public void SetIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> indexingInfo)
        //{
        //    var analyzers = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => GetAnalyzer(kvp.Value));
        //    var indexFieldTypes = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.IndexFieldType);

        //    SearchManager.Instance.SetIndexingInfo(analyzers, indexFieldTypes);
        //    SearchManager.SetPerFieldIndexingInfo(indexingInfo);
        //}

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, 
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames)
        {
            var analyzers = analyzerTypes.ToDictionary(kvp => kvp.Key, kvp => GetAnalyzer(kvp.Value));

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
    }
}
