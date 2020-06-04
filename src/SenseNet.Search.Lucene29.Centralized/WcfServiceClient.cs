using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    public class WcfServiceClient : ClientBase<ISearchServiceContract>, ISearchServiceClient
    {
        #region Search service client creation

        private static DateTime _lastErrorLog;

        private static Binding Binding { get; set; }
        private static EndpointAddress EndPointAddress { get; set; }

        internal static void InitializeServiceEndpoint(Binding binding, EndpointAddress address)
        {
            Binding = binding;
            EndPointAddress = address;

            // re-set the instance
            SearchServiceClient.Instance = GetSearchServiceContract();
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

            // re-create the channel
            SearchServiceClient.Instance = GetSearchServiceContract();
        }
        private static ISearchServiceClient GetSearchServiceContract()
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

        #region Constructors

        public WcfServiceClient() { }
        public WcfServiceClient(string endpointConfigurationName) : base(endpointConfigurationName) { }
        public WcfServiceClient(string endpointConfigurationName, string remoteAddress) :
            base(endpointConfigurationName, remoteAddress)
        { }
        public WcfServiceClient(string endpointConfigurationName, EndpointAddress remoteAddress) :
            base(endpointConfigurationName, remoteAddress)
        { }
        public WcfServiceClient(Binding binding, EndpointAddress remoteAddress) :
            base(binding, remoteAddress)
        { }

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

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query, ServiceQueryContext queryContext)
        {
            return Channel.ExecuteQueryAndProject(query, queryContext);
        }

        public ISearchServiceClient CreateInstance() => GetSearchServiceContract();
        #endregion
    }
}
