using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;
using SenseNet.Security.Configuration;
using SenseNet.Security.Messaging.RabbitMQ;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public class SearchService : GrpcSearch.GrpcSearchBase
    {
        private readonly ILogger<SearchService> _logger;
        private readonly Index.SearchService _indexService;
        private readonly RabbitMqOptions _rabbitMqOptions;


        public SearchService(ILogger<SearchService> logger, Index.SearchService indexService,
            IOptions<RabbitMqOptions> rabbitMqOptions)
        {
            _logger = logger;
            _indexService = indexService;
            _rabbitMqOptions = rabbitMqOptions.Value;
        }

        public override Task<AliveResponse> Alive(AliveRequest request, ServerCallContext context)
        {
            var result = _indexService.Alive();
            return Task.FromResult(new AliveResponse {Alive = result});
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

        public override Task<BackupResponse> Backup(BackupRequest request, ServerCallContext context)
        {
            var result = _indexService.Backup(new Indexing.IndexingActivityStatus
            {
                LastActivityId = request.Status.LastActivityId,
                Gaps = request.Status.Gaps.ToArray()
            }, request.Target);

            return Task.FromResult(new BackupResponse
            {
                Response = JsonConvert.SerializeObject(result)
            });
        }
        public override Task<BackupResponse> QueryBackup(QueryBackupRequest request, ServerCallContext context)
        {
            var result = _indexService.QueryBackup();

            return Task.FromResult(new BackupResponse
            {
                Response = JsonConvert.SerializeObject(result)
            });
        }
        public override Task<BackupResponse> CancelBackup(CancelBackupRequest request, ServerCallContext context)
        {
            var result = _indexService.CancelBackup();

            return Task.FromResult(new BackupResponse
            {
                Response = JsonConvert.SerializeObject(result)
            });
        }

        public override Task<WriteIndexResponse> WriteIndex(WriteIndexRequest request, ServerCallContext context)
        {
            var deletions = request.Deletions
                .Select(del => string.IsNullOrEmpty(del) ? null : Tools.Deserialize<SnTerm>(del))
                .Where(term => term != null).ToArray();
            var updates = request.Updates
                .Select(upd => string.IsNullOrEmpty(upd) ? null : Tools.Deserialize<DocumentUpdate>(upd))
                .Where(upd => upd != null).ToArray();
            var additions = request.Additions
                .Select(add => string.IsNullOrEmpty(add) ? null : IndexDocument.Deserialize(add))
                .Where(add => add != null).ToArray();

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

        public override Task<IndexPropertiesResponse> GetIndexProperties(GetIndexPropertiesRequest request, ServerCallContext context)
        {
            var status = _indexService.GetIndexProperties();

            var ixStatus = new IndexingActivityStatus
            {
                LastActivityId = status.IndexingActivityStatus.LastActivityId,
            };
            ixStatus.Gaps.AddRange(status.IndexingActivityStatus.Gaps);

            var result = new IndexPropertiesResponse();
            result.IndexingActivityStatus = ixStatus;
            result.FieldInfo.Add(status.FieldInfo.ToDictionary(x=>x.Key, x=>x.Value));
            result.VersionIds.Add(status.VersionIds);

            return Task.FromResult(result);
        }
        public override Task<InvertedIndexResponse> GetInvertedIndex(GetInvertedIndexRequest request, ServerCallContext context)
        {
            var invertedIndex = _indexService.GetInvertedIndex(request.FieldName);

            var result = new InvertedIndexResponse();
            result.FieldData.Add(invertedIndex.ToDictionary(
                x=>x.Key,
                x=>
                {
                    var docs = new Documents();
                    docs.Ids.Add(x.Value);
                    return docs;
                }));

            return Task.FromResult(result);
        }
        public override Task<IndexDocumentResponse> GetIndexDocumentByVersionId(GetIndexDocumentRequest request, ServerCallContext context)
        {
            var indexDocument = _indexService.GetIndexDocumentByVersionId(request.Id);

            var result = new IndexDocumentResponse();
            result.IndexDocument.Add(indexDocument);
            return Task.FromResult(result);
        }
        public override Task<IndexDocumentResponse> GetIndexDocumentByDocumentId(GetIndexDocumentRequest request, ServerCallContext context)
        {
            var indexDocument = _indexService.GetIndexDocumentByDocumentId(request.Id);

            var result = new IndexDocumentResponse();
            result.IndexDocument.Add(indexDocument);
            return Task.FromResult(result);
        }

        public override Task<ConfigurationInfoResponse> GetConfigurationInfo(GetConfigurationInfoRequest request, ServerCallContext context)
        {
            var config = _indexService.GetConfigurationInfo();
            config.Add("SearchService_AspNetCore_ApplicationUrl", Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
            config.Add("SearchService_RabbitMq_ServiceUrl", _rabbitMqOptions.ServiceUrl);
            config.Add("SearchService_RabbitMq_MessageExchange", _rabbitMqOptions.MessageExchange);

            var result = new ConfigurationInfoResponse();
            result.Configuration.Add(config);
            return Task.FromResult(result);
        }

        public override Task<HealthResponse> GetHealth(GetHealthRequest request, ServerCallContext context)
        {
            var health = _indexService.GetConfigurationInfo();
            var result = new HealthResponse();
            result.Health.Add(health);
            return Task.FromResult(result);
        }
    }
}
