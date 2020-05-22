using System.Collections.Generic;
using System.ServiceModel;
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
        QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext);
    }
}
