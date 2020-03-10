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
    public class SearchServiceClient : System.ServiceModel.ClientBase<ISearchServiceContract>, ISearchServiceContract
    {
        internal static readonly int RetryCount = 5;
        internal static readonly int RetryWaitMilliseconds = 2000;

        #region Search service contract instance

        private static DateTime _lastErrorLog;
        private static Lazy<ISearchServiceContract> LazyInstance { get; set; } = new Lazy<ISearchServiceContract>(GetSearchServiceContract);

        private static Binding Binding { get; set; }
        private static EndpointAddress EndPointAddress { get; set; }

        internal static void InitializeServiceEndpoint(Binding binding, EndpointAddress address)
        {
            Binding = binding;
            EndPointAddress = address;

            // re-set the instance to force reload on first access
            LazyInstance = new Lazy<ISearchServiceContract>(GetSearchServiceContract);
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
            LazyInstance = new Lazy<ISearchServiceContract>(GetSearchServiceContract);
        }
        private static ISearchServiceContract GetSearchServiceContract()
        {
            // If the caller provided  a binding and address, use that.
            // Otherwise rely on configuration.

            var ssc = Binding != null 
                ? new SearchServiceClient(Binding, EndPointAddress) 
                : new SearchServiceClient();

            ssc.InnerChannel.Faulted += SearchServiceChannelOnFaulted;

            return ssc;
        }

        public static ISearchServiceContract Instance => LazyInstance.Value;

        #endregion

        #region Constructors

        public SearchServiceClient() { }
        public SearchServiceClient(string endpointConfigurationName) : base(endpointConfigurationName) { }
        public SearchServiceClient(string endpointConfigurationName, string remoteAddress) : 
            base(endpointConfigurationName, remoteAddress) {}
        public SearchServiceClient(string endpointConfigurationName, System.ServiceModel.EndpointAddress remoteAddress) : 
            base(endpointConfigurationName, remoteAddress) { }
        public SearchServiceClient(System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress remoteAddress) : 
            base(binding, remoteAddress) { }

        #endregion

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
    }
}
