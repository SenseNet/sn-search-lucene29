//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Lucene.Net.Search;
//using SenseNet.Search.Indexing;
//using SenseNet.Search.Lucene29.Centralized.Service;
//using SenseNet.Search.Querying;

//namespace SenseNet.Search.Lucene29.Centralized
//{
//    public class Lucene29ServiceQueryEngine : IQueryEngine
//    {
//        public QueryResult<int> ExecuteQuery(SnQuery query, IPermissionFilter filter, IQueryContext context)
//        {
//            var lucQuery = Compile(query, context);

//            var lucQueryResult = lucQuery.Execute(filter, context);
//            var hits = lucQueryResult?.Select(x => x.NodeId).ToArray() ?? new int[0];

//            return new QueryResult<int>(hits, lucQuery.TotalCount);
//        }

//        /// <inheritdoc />
//        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, IPermissionFilter filter, IQueryContext context)
//        {
//            var lucQuery = Compile(query, context);

//            var projection = query.Projection ?? IndexFieldName.NodeId;
//            var indexFieldHandler = context.GetPerFieldIndexingInfo(projection).IndexFieldHandler as IIndexValueConverter;
//            var converter = indexFieldHandler == null ? DefaultConverter : indexFieldHandler.GetBack;
//            var lucQueryResult = lucQuery.Execute(filter, context);
//            var hits = lucQueryResult?
//                           .Select(x => x[projection, false])
//                           .Where(r => !string.IsNullOrEmpty(r))
//                           .Select(q => converter(q).ToString())
//                           .ToArray()
//                       ?? new string[0];

//            return new QueryResult<string>(hits, lucQuery.TotalCount);
//        }

//        /* ============================================================================================= */

//        private static readonly Func<string, object> DefaultConverter = s => s;

//        private LucQuery Compile(SnQuery query, IQueryContext context)
//        {
//            var analyzer = SearchManager.Instance.GetAnalyzer();
//            var visitor = new SnQueryToLucQueryVisitor(analyzer, context);
//            visitor.Visit(query.QueryTree);

//            var result = LucQuery.Create(visitor.Result, SearchManager.Instance);
//            result.Skip = query.Skip;
//            result.Top = query.Top;
//            result.SortFields = query.Sort?.Select(s => CreateSortField(s.FieldName, s.Reverse)).ToArray() ?? new SortField[0];
//            result.EnableAutofilters = query.EnableAutofilters;
//            result.EnableLifespanFilter = query.EnableLifespanFilter;
//            result.QueryExecutionMode = query.QueryExecutionMode;
//            result.CountOnly = query.CountOnly;
//            result.CountAllPages = query.CountAllPages;

//            return result;
//        }
//        private static SortField CreateSortField(string fieldName, bool reverse)
//        {
//            //UNDONE: get field indexing info!
//            IPerFieldIndexingInfo info = null;
//            //var info = SearchManager.GetPerFieldIndexingInfo(fieldName);
//            var sortType = SortField.STRING;
//            if (info != null)
//            {
//                fieldName = info.IndexFieldHandler.GetSortFieldName(fieldName);

//                switch (info.IndexFieldHandler.IndexFieldType)
//                {
//                    case IndexValueType.Bool:
//                    case IndexValueType.String:
//                    case IndexValueType.StringArray:
//                        sortType = SortField.STRING;
//                        break;
//                    case IndexValueType.Int:
//                        sortType = SortField.INT;
//                        break;
//                    case IndexValueType.DateTime:
//                    case IndexValueType.Long:
//                        sortType = SortField.LONG;
//                        break;
//                    case IndexValueType.Float:
//                        sortType = SortField.FLOAT;
//                        break;
//                    case IndexValueType.Double:
//                        sortType = SortField.DOUBLE;
//                        break;
//                    default:
//                        throw new ArgumentOutOfRangeException();
//                }
//            }

//            if (sortType == SortField.STRING)
//                return new SortField(fieldName, System.Threading.Thread.CurrentThread.CurrentCulture, reverse);
//            return new SortField(fieldName, sortType, reverse);
//        }
//    }
//}
