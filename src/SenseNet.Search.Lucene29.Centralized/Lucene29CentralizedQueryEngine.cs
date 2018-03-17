using System;
using System.Linq;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    public class Lucene29CentralizedQueryEngine : IQueryEngine
    {
        public QueryResult<int> ExecuteQuery(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            var result = SearchServiceClient.Instance.ExecuteQuery(query, GetQueryContext(query, context));
            return new QueryResult<int>(result.Hits, result.TotalCount);
        }

        private static readonly Func<string, object> DefaultConverter = s => s;

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            //UNDONE: [QUERY] Convert every projected value using the converter.
            //var projection = query.Projection ?? IndexFieldName.NodeId;
            //var indexFieldHandler = context.GetPerFieldIndexingInfo(projection).IndexFieldHandler as IIndexValueConverter;
            //var converter = indexFieldHandler == null ? DefaultConverter : indexFieldHandler.GetBack;

            var result = SearchServiceClient.Instance.ExecuteQueryAndProject(query, GetQueryContext(query, context));
            return new QueryResult<string>(result.Hits, result.TotalCount);
        }

        private static ServiceQueryContext GetQueryContext(SnQuery query, IQueryContext context)
        {
            return new ServiceQueryContext
            {
                UserId = context.UserId,
                FieldLevel = PermissionFilter.GetFieldLevel(query).ToString(),
                DynamicGroups = SystemAccount.Execute(() => Node.Load<User>(context.UserId)?.GetDynamicGroups(0)?.ToArray() ?? new int[0])
            };
        }
    }
}
