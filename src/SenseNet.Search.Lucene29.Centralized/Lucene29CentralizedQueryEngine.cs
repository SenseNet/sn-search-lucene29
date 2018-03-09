using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    public class Lucene29CentralizedQueryEngine : IQueryEngine
    {
        public QueryResult<int> ExecuteQuery(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            //UNDONE: [QUERY] handle permission filter and query context
            var result = SearchServiceClient.Instance.ExecuteQuery(query.ToString());
            return new QueryResult<int>(result.Hits, result.TotalCount);
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            //UNDONE: [QUERY] handle permission filter and query context
            var result = SearchServiceClient.Instance.ExecuteQueryAndProject(query.ToString());
            return new QueryResult<string>(result.Hits, result.TotalCount);
        }
    }
}
