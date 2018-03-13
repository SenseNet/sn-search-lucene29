using System.Collections.Generic;
using System.ServiceModel;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    [ServiceContract(Namespace = "http://SenseNet.ServiceModel.Search.Lucene29")]
    public interface ISearchServiceContract
    {
        //=================================================================================================== Indexing

        [OperationContract]
        void ClearIndex();

        [OperationContract]
        IndexingActivityStatus ReadActivityStatusFromIndex();

        [OperationContract]
        void WriteActivityStatusToIndex(IndexingActivityStatus state);

        [OperationContract]
        void WriteIndex(IEnumerable<SnTerm> deletions, IEnumerable<DocumentUpdate> updates, IEnumerable<IndexDocument> additions);

        //[OperationContract]
        //void SetIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> indexingInfo);

        [OperationContract]
        void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes,
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames);

        //=================================================================================================== Querying

        [OperationContract]
        //QueryResult<int> ExecuteQuery(SnQuery query, IPermissionFilter filter, IQueryContext context);
        //ServiceQueryResultInt ExecuteQuery(SnQuery query);
        QueryResult<int> ExecuteQuery(SnQuery query);

        [OperationContract]
        //QueryResult<string> ExecuteQueryAndProject(SnQuery query, IPermissionFilter filter, IQueryContext context);
        QueryResult<string> ExecuteQueryAndProject(SnQuery query);
    }
}
