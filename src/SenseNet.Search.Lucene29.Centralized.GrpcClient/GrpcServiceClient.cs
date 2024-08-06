using System;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search.Lucene29.Centralized.GrpcService;
using SenseNet.Tools;
using static SenseNet.Search.Lucene29.Centralized.GrpcService.GrpcSearch;
using BackupResponse = SenseNet.Search.Indexing.BackupResponse;
using IndexFieldAnalyzer = SenseNet.Search.Indexing.IndexFieldAnalyzer;
using IndexingActivityStatus = SenseNet.Search.Indexing.IndexingActivityStatus;
using ServiceQueryContext = SenseNet.Search.Lucene29.Centralized.Common.ServiceQueryContext;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    /// <summary>
    /// Implements the <see cref="ISearchServiceContract"/> interface and serves as a
    /// translator between the generic sensenet types and the grpc-specific
    /// communication classes.
    /// </summary>
    public class GrpcServiceClient : ISearchServiceClient
    {
        public ISearchServiceClient CreateInstance() => this;

        private GrpcSearchClient _searchClient;
        private GrpcChannel _channel;

        private IRetrier _retrier;

        private readonly GrpcClientOptions _options;
        private int _maxSendMessageSize;
        private int _maxSendMessageSizeEffective;

        private readonly ILogger<GrpcServiceClient> _logger;

        public GrpcServiceClient(IRetrier retrier, IOptions<GrpcClientOptions> options, ILogger<GrpcServiceClient> logger)
        {
            _retrier = retrier;
            _options = options.Value;
            _logger = logger;
            _maxSendMessageSize = _options.ChannelOptions.MaxSendMessageSize ?? 4_194_304;
            _maxSendMessageSizeEffective = _maxSendMessageSize * 9 / 10; // default: 3_774_873
        }

        [Obsolete("Use the other constructor that is able to work with DI instead.")]
        public GrpcServiceClient(GrpcSearchClient searchClient, GrpcChannel channel, GrpcChannelOptions options)
        {
            _options = new GrpcClientOptions
            {
                ChannelOptions = options,
                ServiceAddress = channel.Target
            };
            _searchClient = searchClient;
            _logger = options?.LoggerFactory?.CreateLogger<GrpcServiceClient>() ?? NullLogger<GrpcServiceClient>.Instance;

            // we pin this object only to be able to shut it down properly later
            _channel = channel;
        }

        public void Start()
        {
            _logger.LogInformation("Starting the Grpc channel...");

            _channel = GrpcChannel.ForAddress(_options.ServiceAddress, _options.ChannelOptions);
            _searchClient = new GrpcSearchClient(_channel);
        }

        public void ShutDown()
        {
            _logger.LogInformation("Shutting down the Grpc channel...");

            // as the channel was created on app start, we need to
            // dispose it properly when the connection is closed
            if (_channel != null)
            {
                _channel.Dispose();
                _channel = null;
            }
        }

        #region ISearchServiceContract implementation

        public bool Alive()
        {
            try
            {
                var result = _searchClient.Alive(new AliveRequest());

                return result.Alive;
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "Alive");
            }
        }

        public void ClearIndex()
        {
            try
            {
                _searchClient.ClearIndex(new ClearIndexRequest());
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "ClearIndex");
            }
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            GrpcService.IndexingActivityStatus result;

            try
            {
                result = _searchClient.ReadActivityStatusFromIndex(new ReadActivityStatusRequest());
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "ReadActivityStatusFromIndex");
            }

            return new IndexingActivityStatus() 
            { 
                LastActivityId = result.LastActivityId,
                Gaps = result.Gaps.ToArray()
            };
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            try
            {
                _logger.LogTrace($"Writing activity status {state}");
                _searchClient.WriteActivityStatusToIndex(state.ToGrpcActivityStatus());
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "WriteActivityStatusToIndex");
            }
        }

        public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            try
            {
                _logger.LogTrace($"Creating index backup in {backupDirectoryPath}");

                var response = _searchClient.Backup(new BackupRequest
                {
                    Status = state.ToGrpcActivityStatus(),
                    Target = backupDirectoryPath
                });

                return JsonConvert.DeserializeObject<BackupResponse>(response.Response);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "Backup");
            }
        }

        public BackupResponse QueryBackup()
        {
            try
            {
                var response = _searchClient.QueryBackup(new QueryBackupRequest());

                return JsonConvert.DeserializeObject<BackupResponse>(response.Response);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "QueryBackup");
            }
        }

        public BackupResponse CancelBackup()
        {
            try
            {
                var response = _searchClient.CancelBackup(new CancelBackupRequest());

                return JsonConvert.DeserializeObject<BackupResponse>(response.Response);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "CancelBackup");
            }
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, IDictionary<string, IndexValueType> indexFieldTypes, IDictionary<string, string> sortFieldNames)
        {
            try
            {
                var request = new SetIndexingInfoRequest();

                foreach (var (key, indexFieldAnalyzer) in analyzerTypes)
                    request.AnalyzerTypes.Add(key, (GrpcService.IndexFieldAnalyzer)indexFieldAnalyzer);
                foreach (var (key, indexValueType) in indexFieldTypes)
                    request.IndexFieldTypes.Add(key, (GrpcService.IndexValueType)indexValueType);
                foreach (var (key, fieldName) in sortFieldNames)
                    request.SortFieldNames.Add(key, fieldName);

                _searchClient.SetIndexingInfo(request);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "SetIndexingInfo");
            }
        }

        public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
        {
            if (deletions != null)
                SendByPartitionsAsync(deletions,
                    snTerm => snTerm.Serialize(),
                    (item, request) => { request.Deletions.Add(item);},
                    CancellationToken.None).GetAwaiter().GetResult(); ;
            if (updates != null)
                SendByPartitionsAsync(updates,
                    documentUpdate => documentUpdate.Serialize(),
                    (item, request) => { request.Updates.Add(item); },
                    CancellationToken.None).GetAwaiter().GetResult(); ;
            if (additions != null)
                SendByPartitionsAsync(additions,
                    indexDocument => indexDocument.Serialize(),
                    (item, request) => { request.Additions.Add(item); },
                    CancellationToken.None).GetAwaiter().GetResult();
        }

        private async Task SendByPartitionsAsync<T>(T[] items, Func<T, string> serialize,
            Action<string, GrpcService.WriteIndexRequest> addToRequest, CancellationToken cancel)
        {
            var request = new GrpcService.WriteIndexRequest();
            var length = 0;
            foreach (var item in items)
            {
                var serialized = serialize(item);
                if (length + serialized.Length > _maxSendMessageSizeEffective)
                {
                    await SendWriteIndexRequestAsync(request, cancel).ConfigureAwait(false);
                    request = new GrpcService.WriteIndexRequest();
                    length = 0;
                }
                addToRequest(serialized, request);
                length += serialized.Length;
            }

            if (length > 0)
                await SendWriteIndexRequestAsync(request, cancel).ConfigureAwait(false);
        }
        private async Task SendWriteIndexRequestAsync(WriteIndexRequest request, CancellationToken cancel)
        {
            _logger.LogTrace($"WriteIndexRequest: deletions count: {request.Deletions?.Count ?? 0}, " +
                             $"updates count: {request.Updates?.Count ?? 0}, " +
                             $"updates size: {request.Updates?.Sum(x => x.Length) ?? 0}, " +
                             $"additions count: {request.Additions?.Count ?? 0}, " +
                             $"additions size: {request.Additions?.Sum(x => x.Length) ?? 0}");
            await _retrier.RetryAsync(
                action: async () =>
                {
                    await _searchClient.WriteIndexAsync(request);
                },
                shouldRetryOnError: (e, i) =>
                {
                    _logger.LogWarning($"WriteIndex failed. Iteration: {i}, Error: {e.Message}");
                    return true;
                },
                onAfterLastIteration: (e, i) =>
                {
                    _logger.LogError(e, $"WriteIndex failed after {i} iteration.");
                }
                , cancel: cancel);

            //try
            //{
            //    _searchClient.WriteIndex(request);
            //}
            //catch (Exception ex)
            //{
            //    throw LogAndFormatException(ex, "WriteIndex");
            //}
        }

        public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
        {
            var context = new GrpcService.ServiceQueryContext() 
            {
                UserId = queryContext.UserId,
                FieldLevel = queryContext.FieldLevel                
            };
            context.DynamicGroups.AddRange(queryContext.DynamicGroups);

            try
            {
                var result = _searchClient.ExecuteQuery(new GrpcService.QueryRequest()
                {
                    Query = Tools.SerializeSnQuery(query),
                    Context = context
                });

                return new QueryResult<int>(result.Hits, result.TotalCount);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "ExecuteQuery");
            }
        }
        public async Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
        {
            var context = new GrpcService.ServiceQueryContext()
            {
                UserId = queryContext.UserId,
                FieldLevel = queryContext.FieldLevel
            };
            context.DynamicGroups.AddRange(queryContext.DynamicGroups);

            try
            {
                var result = await _searchClient.ExecuteQueryAsync(new GrpcService.QueryRequest()
                {
                    Query = Tools.SerializeSnQuery(query),
                    Context = context
                }, cancellationToken: cancel);

                return new QueryResult<int>(result.Hits, result.TotalCount);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "ExecuteQuery");
            }
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            var context = new GrpcService.ServiceQueryContext()
            {
                UserId = queryContext.UserId,
                FieldLevel = queryContext.FieldLevel
            };
            context.DynamicGroups.AddRange(queryContext.DynamicGroups);

            try
            {
                var result = _searchClient.ExecuteQueryAndProject(new GrpcService.QueryRequest()
                {
                    Query = Tools.SerializeSnQuery(query),
                    Context = context
                });

                return new QueryResult<string>(result.Hits, result.TotalCount);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "ExecuteQueryAndProject");
            }
        }
        public async Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
        {
            var context = new GrpcService.ServiceQueryContext()
            {
                UserId = queryContext.UserId,
                FieldLevel = queryContext.FieldLevel
            };
            context.DynamicGroups.AddRange(queryContext.DynamicGroups);

            try
            {
                var result = await _searchClient.ExecuteQueryAndProjectAsync(new GrpcService.QueryRequest()
                {
                    Query = Tools.SerializeSnQuery(query),
                    Context = context
                }, cancellationToken: cancel);

                return new QueryResult<string>(result.Hits, result.TotalCount);
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "ExecuteQueryAndProject");
            }
        }


        public IndexProperties GetIndexProperties()
        {
            GrpcService.IndexPropertiesResponse result;

            try
            {
                _logger.LogTrace("Getting index properties");
                result = _searchClient.GetIndexProperties(new GetIndexPropertiesRequest());
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "GetIndexProperties");
            }

            return new IndexProperties
            {
                IndexingActivityStatus = new IndexingActivityStatus
                {
                    LastActivityId = result.IndexingActivityStatus.LastActivityId,
                    Gaps = result.IndexingActivityStatus.Gaps.ToArray()
                },
                FieldInfo = result.FieldInfo,
                VersionIds = result.VersionIds
            };
        }
        public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
        {
            GrpcService.InvertedIndexResponse result;

            try
            {
                _logger.LogTrace($"Getting inverted index of '{fieldName}' field.");
                result = _searchClient.GetInvertedIndex(new GetInvertedIndexRequest {FieldName = fieldName});
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "GetInvertedIndexAsync");
            }

            return result.FieldData.ToDictionary(
                x=>x.Key,
                x=>x.Value.Ids.ToList());
        }
        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            GrpcService.IndexDocumentResponse result;

            try
            {
                _logger.LogTrace($"Getting index document by versionId {versionId}.");
                result = _searchClient.GetIndexDocumentByVersionId(new GetIndexDocumentRequest { Id = versionId});
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "GetIndexDocumentByVersionId");
            }

            return result.IndexDocument;
        }
        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            GrpcService.IndexDocumentResponse result;

            try
            {
                _logger.LogTrace($"Getting index document by documentId {documentId}.");
                result = _searchClient.GetIndexDocumentByDocumentId(new GetIndexDocumentRequest { Id = documentId });
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "GetIndexDocumentByDocumentId");
            }

            return result.IndexDocument;
        }

        public IDictionary<string, string> GetConfigurationInfo()
        {
            GrpcService.ConfigurationInfoResponse result;

            try
            {
                _logger.LogTrace($"Getting configuration info.");
                result = _searchClient.GetConfigurationInfo(new GetConfigurationInfoRequest());
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "getting configuration info");
            }

            var configData = result.Configuration
                .ToDictionary(x => x.Key, x => x.Value);
            configData.Add("GRPC_CLIENT:MaxReceiveMessageSize", _options.ChannelOptions.MaxReceiveMessageSize?.ToString() ?? "null");
            configData.Add("GRPC_CLIENT:MaxSendMessageSize", _options.ChannelOptions.MaxSendMessageSize?.ToString() ?? "null");

            return configData;
        }

        public IDictionary<string, string> GetHealth()
        {
            GrpcService.HealthResponse result;

            try
            {
                _logger.LogTrace($"Getting health info.");
                result = _searchClient.GetHealth(new GetHealthRequest());
            }
            catch (Exception ex)
            {
                throw LogAndFormatException(ex, "getting health info");
            }

            return result.Health;
        }

        #endregion

        #region Helper methods

        private Exception LogAndFormatException(Exception ex, string source)
        {
            var msg = $"Error in {source}: {ex.Message}";
            _logger.LogError(ex, msg);
            throw new InvalidOperationException(msg, ex);
        }

        #endregion
    }
}
