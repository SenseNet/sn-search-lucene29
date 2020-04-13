using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public class SearchService : GrpcSearch.GrpcSearchBase
    {
        private readonly ILogger<SearchService> _logger;
        public SearchService(ILogger<SearchService> logger)
        {
            _logger = logger;
        }

        public override Task<ClearIndexResponse> ClearIndex(ClearIndexRequest request, ServerCallContext context)
        {
            return base.ClearIndex(request, context);
        }
        public override Task<SetIndexingInfoResponse> SetIndexingInfo(SetIndexingInfoRequest request, ServerCallContext context)
        {
            return base.SetIndexingInfo(request, context);
        }
        public override Task<WriteActivityStatusResponse> WriteActivityStatusToIndex(IndexingActivityStatus request, ServerCallContext context)
        {
            return base.WriteActivityStatusToIndex(request, context);
        }        
        public override Task<IndexingActivityStatus> ReadActivityStatusFromIndex(ReadActivityStatusRequest readActivityStatus,
            ServerCallContext context)
        {
            return Task.FromResult(new IndexingActivityStatus
            {
                LastActivityId = 123
            });
        }
        public override Task<WriteIndexResponse> WriteIndex(WriteIndexRequest request, ServerCallContext context)
        {
            return base.WriteIndex(request, context);
        }
        public override Task<QueryResultIds> ExecuteQuery(QueryRequest request, ServerCallContext context)
        {
            return base.ExecuteQuery(request, context);
        }
        public override Task<QueryResultProjections> ExecuteQueryAndProject(QueryRequest request, ServerCallContext context)
        {
            return base.ExecuteQueryAndProject(request, context);
        }
    }
}
