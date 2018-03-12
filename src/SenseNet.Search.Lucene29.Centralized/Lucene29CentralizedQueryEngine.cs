using System;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    public class Lucene29CentralizedQueryEngine : IQueryEngine
    {
        public QueryResult<int> ExecuteQuery(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            //UNDONE: [QUERY] handle permission filter and query context
            var result = SearchServiceClient.Instance.ExecuteQuery(query);
            return new QueryResult<int>(result.Hits, result.TotalCount);
        }

        private static readonly Func<string, object> DefaultConverter = s => s;

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            //UNDONE: [QUERY] handle permission filter and query context

            //UNDONE: [QUERY] Reference Services package and convert every projected value using the converter.
            //var projection = query.Projection ?? IndexFieldName.NodeId;
            //var indexFieldHandler = context.GetPerFieldIndexingInfo(projection).IndexFieldHandler as IIndexValueConverter;
            //var converter = indexFieldHandler == null ? DefaultConverter : indexFieldHandler.GetBack;

            var result = SearchServiceClient.Instance.ExecuteQueryAndProject(query);
            return new QueryResult<string>(result.Hits, result.TotalCount);
        }
    }
}
