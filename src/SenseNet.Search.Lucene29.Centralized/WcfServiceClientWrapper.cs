using System.Collections.Generic;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    /// <summary>
    /// Internal wrapper class for <see cref="WcfServiceClient"/> to make sure
    /// that the client object is disposed correctly.
    /// </summary>
    class WcfServiceClientWrapper : ISearchServiceClient
    {
        public bool Alive()
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.Alive();
            }
        }

        public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.Backup(state, backupDirectoryPath);
            }
        }

        public BackupResponse CancelBackup()
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.CancelBackup();
            }
        }

        public void ClearIndex()
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                client.ClearIndex();
            }
        }

        public ISearchServiceClient CreateInstance()
        {
            return this;
        }

        public void Start()
        {
            // do nothing
        }

        public void ShutDown()
        {
            // do nothing
        }

        public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.ExecuteQuery(query, queryContext);
            }
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.ExecuteQueryAndProject(query, queryContext);
            }
        }

        public BackupResponse QueryBackup()
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.QueryBackup();
            }
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.ReadActivityStatusFromIndex();
            }
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, 
            IDictionary<string, IndexValueType> indexFieldTypes, IDictionary<string, string> sortFieldNames)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                client.SetIndexingInfo(analyzerTypes, indexFieldTypes, sortFieldNames);
            }
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                client.WriteActivityStatusToIndex(state);
            }
        }

        public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                client.WriteIndex(deletions, updates, additions);
            }
        }

        public IndexProperties GetIndexProperties()
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.GetIndexProperties();
            }
        }
        public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.GetInvertedIndex(fieldName);
            }
        }
        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.GetIndexDocumentByVersionId(versionId);
            }
        }
        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            using (var client = WcfServiceClient.GetSearchServiceContract())
            {
                return client.GetIndexDocumentByDocumentId(documentId);
            }
        }

    }
}
