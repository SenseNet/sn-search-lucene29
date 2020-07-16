using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;
using System.Collections.Generic;
using System.Linq;
using Grpc.Net.Client;
using Newtonsoft.Json;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search.Lucene29.Centralized.GrpcService;
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
        /* =================================================== ISearchServiceClient */

        public ISearchServiceClient CreateInstance() => this;

        /* =================================================== ISearchServiceContract */

        private readonly GrpcSearchClient _searchClient;
        private readonly GrpcChannel _channel;
        public GrpcServiceClient(GrpcSearchClient searchClient, GrpcChannel channel)
        {
            _searchClient = searchClient;

            // we pin this object only to be able to shut it down properly later
            _channel = channel;
        }

        public void ShutDown()
        {
            // as the channel was created on app start, we need to
            // dispose it properly when the connection is closed
            _channel?.Dispose();
        }

        #region ISearchServiceContract implementation

        public bool Alive()
        {
            var result = _searchClient.Alive(new AliveRequest());

            return result.Alive;
        }

        public void ClearIndex()
        {
            _searchClient.ClearIndex(new ClearIndexRequest());
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            var result = _searchClient.ReadActivityStatusFromIndex(new GrpcService.ReadActivityStatusRequest());

            return new IndexingActivityStatus() 
            { 
                LastActivityId = result.LastActivityId,
                Gaps = result.Gaps.ToArray()
            };
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            _searchClient.WriteActivityStatusToIndex(state.ToGrpcActivityStatus());
        }

        public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            var response = _searchClient.Backup(new BackupRequest
            {
                Status = state.ToGrpcActivityStatus(),
                Target = backupDirectoryPath
            });

            return JsonConvert.DeserializeObject<BackupResponse>(response.Response);
        }

        public BackupResponse QueryBackup()
        {
            var response = _searchClient.QueryBackup(new QueryBackupRequest());

            return JsonConvert.DeserializeObject<BackupResponse>(response.Response);
        }

        public BackupResponse CancelBackup()
        {
            var response = _searchClient.CancelBackup(new CancelBackupRequest());

            return JsonConvert.DeserializeObject<BackupResponse>(response.Response);
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, IDictionary<string, IndexValueType> indexFieldTypes, IDictionary<string, string> sortFieldNames)
        {
            var request = new GrpcService.SetIndexingInfoRequest();

            foreach (var at in analyzerTypes)            
                request.AnalyzerTypes.Add(at.Key, (GrpcService.IndexFieldAnalyzer)at.Value);
            foreach (var ift in indexFieldTypes)
                request.IndexFieldTypes.Add(ift.Key, (GrpcService.IndexValueType)ift.Value);
            foreach (var sfn in sortFieldNames)
                request.SortFieldNames.Add(sfn.Key, sfn.Value);

            _searchClient.SetIndexingInfo(request);
        }

        public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
        {
            var request = new GrpcService.WriteIndexRequest();
            
            request.Deletions.AddRange(deletions.Select(del => Tools.Serialize(del)));
            request.Updates.AddRange(updates.Select(upd => Tools.Serialize(upd)));
            request.Additions.AddRange(additions.Select(add => Tools.Serialize(add)));

            _searchClient.WriteIndex(request);
        }

        public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
        {
            var context = new GrpcService.ServiceQueryContext() 
            {
                UserId = queryContext.UserId,
                FieldLevel = queryContext.FieldLevel                
            };
            context.DynamicGroups.AddRange(queryContext.DynamicGroups);

            var result = _searchClient.ExecuteQuery(new GrpcService.QueryRequest()
            {
                Query = Tools.Serialize(query),
                Context = context                
            });

            return new QueryResult<int>(result.Hits, result.TotalCount);
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            var context = new GrpcService.ServiceQueryContext()
            {
                UserId = queryContext.UserId,
                FieldLevel = queryContext.FieldLevel
            };
            context.DynamicGroups.AddRange(queryContext.DynamicGroups);

            var result = _searchClient.ExecuteQueryAndProject(new GrpcService.QueryRequest()
            {
                Query = Tools.Serialize(query),
                Context = context
            });

            return new QueryResult<string>(result.Hits, result.TotalCount);
        }
        #endregion
    }
}
