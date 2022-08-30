using System;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;
using Retrier = SenseNet.Tools.Retrier;

namespace SenseNet.Search.Lucene29
{
    public interface IServiceQueryContextFactory
    {
        ServiceQueryContext Create(SnQuery query, IQueryContext context);
    }

    /// <summary>
    /// Centralized query engine built using Lucene 2.9.
    /// It requires a central search service accessible for all web servers at all times. All operations
    /// are routed towards that service with a short retry period.
    /// </summary>
    public class Lucene29CentralizedQueryEngine : IQueryEngine
    {
        public IServiceQueryContextFactory ServiceQueryContextFactory { get; set; }

        public QueryResult<int> ExecuteQuery(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            var result = Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.ExecuteQuery(query, GetQueryContext(query, context)));

            return new QueryResult<int>(result.Hits, result.TotalCount);
        }

        public async Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, IPermissionFilter filter, IQueryContext context, CancellationToken cancel)
        {
            var result = await Retrier.RetryAsync<QueryResult<int>>(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds,
                async () => await SearchServiceClient.Instance.ExecuteQueryAsync(
                    query, GetQueryContext(query, context), cancel),
                (r, count, error) => error == null);

            return new QueryResult<int>(result.Hits, result.TotalCount);
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, IPermissionFilter filter, IQueryContext context)
        {
            var projection = query.Projection ?? IndexFieldName.NodeId;
            var converter = !(context.GetPerFieldIndexingInfo(projection).IndexFieldHandler is IIndexValueConverter indexFieldHandler)
                ? DefaultConverter
                : indexFieldHandler.GetBack;

            var result = Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.ExecuteQueryAndProject(query, GetQueryContext(query, context)));

            return new QueryResult<string>(result.Hits.Select(h => converter(h)?.ToString()), result.TotalCount);
        }

        public async Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, IPermissionFilter filter, IQueryContext context,
            CancellationToken cancel)
        {
            var projection = query.Projection ?? IndexFieldName.NodeId;
            var converter = !(context.GetPerFieldIndexingInfo(projection).IndexFieldHandler is IIndexValueConverter indexFieldHandler)
                ? DefaultConverter
                : indexFieldHandler.GetBack;

            var result = await Retrier.RetryAsync< QueryResult<string>>(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds,
                async () => await SearchServiceClient.Instance.ExecuteQueryAndProjectAsync(
                    query, GetQueryContext(query, context), cancel),
                (r, count, error) => error == null);

            return new QueryResult<string>(result.Hits.Select(h => converter(h)?.ToString()), result.TotalCount);
        }

        private static readonly Func<string, object> DefaultConverter = s => s;

        private ServiceQueryContext GetQueryContext(SnQuery query, IQueryContext context)
        {
            return ServiceQueryContextFactory?.Create(query, context) ??
                   new ServiceQueryContext
                   {
                       UserId = context.UserId,
                       FieldLevel = PermissionFilter.GetFieldLevel(query).ToString(),
                       DynamicGroups = SystemAccount.Execute(() =>
                           Node.Load<User>(context.UserId)?.GetDynamicGroups(0)?.ToArray() ?? new int[0])
                   };
        }
    }
}
