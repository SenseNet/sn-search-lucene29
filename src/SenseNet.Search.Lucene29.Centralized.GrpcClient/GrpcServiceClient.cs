using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;
using System.Collections.Generic;
using System.Linq;
using static SenseNet.Search.Lucene29.Centralized.GrpcService.GrpcSearch;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    public class GrpcServiceClient : ISearchServiceContract
    {
        private readonly GrpcSearchClient _searchClient;
        public GrpcServiceClient(GrpcSearchClient searchClient)
        {
            _searchClient = searchClient;
        }
        public void ClearIndex()
        {
            _searchClient.ClearIndex(new GrpcService.ClearIndexRequest());            
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
            var request = new GrpcService.IndexingActivityStatus()
            { 
                LastActivityId = state.LastActivityId
            };
            request.Gaps.AddRange(state.Gaps);

            _searchClient.WriteActivityStatusToIndex(request);
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
    }
}
