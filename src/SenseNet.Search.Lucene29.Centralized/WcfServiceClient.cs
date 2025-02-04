using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "<Pending>")]
    [Obsolete("Do not use this technology anymore.", true)]
    public class WcfServiceClient : ClientBase<ISearchServiceContract>, ISearchServiceClient, IDisposable
    {
        #region Search service client creation

        private static DateTime _lastErrorLog;

        private static Binding Binding { get; set; }
        private static EndpointAddress EndPointAddress { get; set; }

        internal static void InitializeServiceEndpoint(Binding binding, EndpointAddress address)
        {
            Binding = binding;
            EndPointAddress = address;

            // Set a singleton wrapper instance that will create and dispose
            // client objects when service methods are called.
            SearchServiceClient.Instance = new WcfServiceClientWrapper();
        }

        private static void SearchServiceChannelOnFaulted(object sender, EventArgs eventArgs)
        {
            SnTrace.Index.WriteError("Centralized search service channel error.");

            // log an error once per minute
            if (_lastErrorLog.AddMinutes(1) < DateTime.UtcNow)
            {
                SnLog.WriteError("Centralized search service channel error.");
                _lastErrorLog = DateTime.UtcNow;
            }
        }
        internal static WcfServiceClient GetSearchServiceContract()
        {
            // If the caller provided  a binding and address, use that.
            // Otherwise rely on configuration.

            var ssc = Binding != null
                ? new WcfServiceClient(Binding, EndPointAddress)
                : new WcfServiceClient();

            ssc.InnerChannel.Faulted += SearchServiceChannelOnFaulted;

            return ssc;
        }

        #endregion

        #region Constructors and Dispose

        public WcfServiceClient() { }
        //public WcfServiceClient(string endpointConfigurationName) : base(endpointConfigurationName) { }
        //public WcfServiceClient(string endpointConfigurationName, string remoteAddress) :
        //    base(endpointConfigurationName, remoteAddress)
        //{ }
        //public WcfServiceClient(string endpointConfigurationName, EndpointAddress remoteAddress) :
        //    base(endpointConfigurationName, remoteAddress)
        //{ }
        public WcfServiceClient(Binding binding, EndpointAddress remoteAddress) :
            base(binding, remoteAddress)
        { }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }
        /// <summary>
        /// Dispose worker method. Handles graceful shutdown of the
        /// client even if it is in a faulted state.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) 
                return;

            try
            {
                if (State != CommunicationState.Faulted)
                {
                    Close();
                }
            }
            finally
            {
                if (State != CommunicationState.Closed)
                {
                    Abort();
                }
            }
        }

        #endregion

        #region ISearchServiceContract implementation
        public bool Alive()
        {
            return Channel.Alive();
        }

        public void ClearIndex()
        {
            Channel.ClearIndex();
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            return Channel.ReadActivityStatusFromIndex();
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            Channel.WriteActivityStatusToIndex(state);
        }

        public BackupResponse Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            return Channel.Backup(state, backupDirectoryPath);
        }

        public BackupResponse QueryBackup()
        {
            return Channel.QueryBackup();
        }

        public BackupResponse CancelBackup()
        {
            return Channel.CancelBackup();
        }

        public void WriteIndex(SnTerm[] deletions, DocumentUpdate[] updates, IndexDocument[] additions)
        {
            Channel.WriteIndex(deletions, updates, additions);
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes,
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames)
        {
            Channel.SetIndexingInfo(analyzerTypes, indexFieldTypes, sortFieldNames);
        }

        public QueryResult<int> ExecuteQuery(SnQuery query, ServiceQueryContext queryContext)
        {
            return Channel.ExecuteQuery(query, queryContext);
        }

        public Task<QueryResult<int>> ExecuteQueryAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
        {
            throw new NotImplementedException();
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            return Channel.ExecuteQueryAndProject(query, queryContext);
        }

        public Task<QueryResult<string>> ExecuteQueryAndProjectAsync(SnQuery query, ServiceQueryContext queryContext, CancellationToken cancel)
        {
            throw new NotImplementedException();
        }

        public IndexProperties GetIndexProperties()
        {
            return Channel.GetIndexProperties();
        }
        public IDictionary<string, List<int>> GetInvertedIndex(string fieldName)
        {
            return Channel.GetInvertedIndex(fieldName);
        }
        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            return Channel.GetIndexDocumentByVersionId(versionId);
        }
        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            return Channel.GetIndexDocumentByDocumentId(documentId);
        }

        public IDictionary<string, string> GetConfigurationInfo()
        {
            return Channel.GetConfigurationInfo();
        }
        public IDictionary<string, string> GetHealth()
        {
            return Channel.GetHealth();
        }

        public ISearchServiceClient CreateInstance() => GetSearchServiceContract();

        public void Start()
        {
            // do nothing
        }

        public void ShutDown()
        {
            // do nothing
        }

        #endregion
    }
}
