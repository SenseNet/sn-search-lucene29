using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public class SearchService : GrpcSearch.GrpcSearchBase
    {
        private readonly ILogger<SearchService> _logger;
        private readonly Index.SearchService _indexService;

        public SearchService(ILogger<SearchService> logger, Index.SearchService indexService)
        {
            _logger = logger;
            _indexService = indexService;
        }

        public override Task<ClearIndexResponse> ClearIndex(ClearIndexRequest request, ServerCallContext context)
        {
            _indexService.ClearIndex();

            return Task.FromResult(new ClearIndexResponse());
        }
        public override Task<SetIndexingInfoResponse> SetIndexingInfo(SetIndexingInfoRequest request, ServerCallContext context)
        {            
            _indexService.SetIndexingInfo(
                request.AnalyzerTypes.ToDictionary(kv => kv.Key, kv => (Indexing.IndexFieldAnalyzer)(int)kv.Value),
                request.IndexFieldTypes.ToDictionary(kv => kv.Key, kv => (Search.IndexValueType)(int)kv.Value),
                request.SortFieldNames.ToDictionary(kv => kv.Key, kv => kv.Value));

            return Task.FromResult(new SetIndexingInfoResponse());
        }
        public override Task<WriteActivityStatusResponse> WriteActivityStatusToIndex(IndexingActivityStatus request, ServerCallContext context)
        {
            var status = new Indexing.IndexingActivityStatus()
            { 
                LastActivityId = request.LastActivityId,
                Gaps = request.Gaps.ToArray()
            };

            _indexService.WriteActivityStatusToIndex(status);

            return Task.FromResult(new WriteActivityStatusResponse());
        }        
        public override Task<IndexingActivityStatus> ReadActivityStatusFromIndex(ReadActivityStatusRequest readActivityStatus,
            ServerCallContext context)
        {
            var status = _indexService.ReadActivityStatusFromIndex();
            var result = new IndexingActivityStatus
            {
                LastActivityId = status.LastActivityId
            };
            result.Gaps.AddRange(status.Gaps);

            return Task.FromResult(result);
        }
        public override Task<WriteIndexResponse> WriteIndex(WriteIndexRequest request, ServerCallContext context)
        {
            var deletions = request.Deletions.Select(del => Tools.Deserialize<SnTerm>(del)).ToArray();
            var updates = request.Updates.Select(upd => Tools.Deserialize<DocumentUpdate>(upd)).ToArray();
            var additions = request.Additions.Select(add => IndexDocument.Deserialize(add)).ToArray();

            _indexService.WriteIndex(deletions, updates, additions);

            return Task.FromResult(new WriteIndexResponse());
        }
        public override Task<QueryResultIds> ExecuteQuery(QueryRequest request, ServerCallContext context)
        {
            var queryResult = _indexService.ExecuteQuery(
                Tools.Deserialize<SnQuery>(request.Query), 
                new Common.ServiceQueryContext() 
                { 
                    UserId = request.Context.UserId,
                    DynamicGroups = request.Context.DynamicGroups.ToArray(),
                    FieldLevel = request.Context.FieldLevel
                });

            var result = new QueryResultIds()
            {
                TotalCount = queryResult.TotalCount
            };
            result.Hits.AddRange(queryResult.Hits);

            return Task.FromResult(result);
        }
        public override Task<QueryResultProjections> ExecuteQueryAndProject(QueryRequest request, ServerCallContext context)
        {
            var queryResult = _indexService.ExecuteQueryAndProject(
                Tools.Deserialize<SnQuery>(request.Query),
                new Common.ServiceQueryContext()
                {
                    UserId = request.Context.UserId,
                    DynamicGroups = request.Context.DynamicGroups.ToArray(),
                    FieldLevel = request.Context.FieldLevel
                });

            var result = new QueryResultProjections()
            {
                TotalCount = queryResult.TotalCount
            };
            result.Hits.AddRange(queryResult.Hits);

            return Task.FromResult(result);
        }
    }
}
