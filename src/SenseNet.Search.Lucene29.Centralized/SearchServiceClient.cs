using System.Collections.Generic;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    public class SearchServiceClient
    {
        private static ISearchServiceClient ClientPrototype { get; set; } = new NullServiceClient();

        public static ISearchServiceClient Instance
        {
            get => ClientPrototype?.CreateInstance();
            internal set => ClientPrototype = value;
        }

        /* =================================================== -------------------- */

        internal static readonly int RetryCount = 5;
        internal static readonly int RetryWaitMilliseconds = 2000;
    }

    /// <summary>
    /// Empty service client implementation that serves as a placeholder.
    /// </summary>
    internal class NullServiceClient : ISearchServiceClient
    {
        public bool Alive()
        {
            SnTrace.Index.Write("NullServiceClient.Alive is not implemented.");
            return true;
        }

        public void ClearIndex()
        {
            SnTrace.Index.Write("NullServiceClient.ClearIndex is not implemented.");
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            SnTrace.Index.Write("NullServiceClient.ReadActivityStatusFromIndex is not implemented.");
            return IndexingActivityStatus.Startup;
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            SnTrace.Index.Write("NullServiceClient.WriteActivityStatusToIndex is not implemented.");
        }

        public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            throw new System.NotImplementedException("NullServiceClient.Backup is not implemented.");
        }

        public BackupResponse QueryBackup()
        {
            throw new System.NotImplementedException("NullServiceClient.QueryBackup is not implemented.");
        }

        public BackupResponse CancelBackup()
        {
            throw new System.NotImplementedException("NullServiceClient.CancelBackup is not implemented.");
        }

        public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
        {
            SnTrace.Index.Write("NullServiceClient.WriteIndex is not implemented.");
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, IDictionary<string, IndexValueType> indexFieldTypes, IDictionary<string, string> sortFieldNames)
        {
            SnTrace.Index.Write("NullServiceClient.SetIndexingInfo is not implemented.");
        }

        public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
        {
            SnTrace.Index.Write("NullServiceClient.ExecuteQuery is not implemented.");
            return QueryResult<int>.Empty;
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            SnTrace.Index.Write("NullServiceClient.ExecuteQueryAndProject is not implemented.");
            return QueryResult<string>.Empty;
        }

        public IndexProperties GetIndexProperties()
        {
            throw new System.NotImplementedException("NullServiceClient.GetIndexProperties is not implemented.");
        }
        public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
        {
            throw new System.NotImplementedException("NullServiceClient.GetInvertedIndex is not implemented.");
        }
        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            throw new System.NotImplementedException("NullServiceClient.GetIndexDocumentByVersionId is not implemented.");
        }
        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            throw new System.NotImplementedException("NullServiceClient.GetIndexDocumentByDocumentId is not implemented.");
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
    }
}
