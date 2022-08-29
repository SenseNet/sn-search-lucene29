using System.Collections.Generic;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.Common
{
    [ServiceContract(Namespace = "http://SenseNet.ServiceModel.Search.Lucene29")]
    public interface ISearchServiceContract
    {
        [OperationContract]
        bool Alive();

        //=================================================================================================== Indexing

        [OperationContract]
        void ClearIndex();

        [OperationContract]
        IndexingActivityStatus ReadActivityStatusFromIndex();

        [OperationContract]
        void WriteActivityStatusToIndex(IndexingActivityStatus state);

        [OperationContract]
        BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath);

        [OperationContract]
        BackupResponse QueryBackup();

        [OperationContract]
        BackupResponse CancelBackup();

        [OperationContract]
        void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions);

        [OperationContract]
        void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes,
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames);

        //=================================================================================================== Querying

        [OperationContract]
        QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext);
        [OperationContract]
        Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel);

        [OperationContract]
        QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext);
        [OperationContract]
        Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel);

        //=================================================================================================== Save Index

        [OperationContract]
        IndexProperties GetIndexProperties();

        [OperationContract]
        IDictionary<string, List<int>> GetInvertedIndex(string fieldName);

        [OperationContract]
        IDictionary<string, string> GetIndexDocumentByVersionId(int versionId);

        [OperationContract]
        IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId);
    }
}
