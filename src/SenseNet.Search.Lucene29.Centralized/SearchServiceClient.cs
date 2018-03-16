using System;
using System.Collections.Generic;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized
{
    public class SearchServiceClient : System.ServiceModel.ClientBase<ISearchServiceContract>, ISearchServiceContract
    {
        //UNDONE: re-create the client if the connection fails
        private static readonly Lazy<ISearchServiceContract> LazyInstance = new Lazy<ISearchServiceContract>(() => new SearchServiceClient());
        public static ISearchServiceContract Instance => LazyInstance.Value;

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

        public void WriteIndex(IEnumerable<SnTerm> deletions, IEnumerable<DocumentUpdate> updates, IEnumerable<IndexDocument> additions)
        {
            Channel.WriteIndex(deletions, updates, additions);
        }

        public void SetIndexingInfo(IDictionary<string, IndexFieldAnalyzer> analyzerTypes, 
            IDictionary<string, IndexValueType> indexFieldTypes,
            IDictionary<string, string> sortFieldNames)
        {
            Channel.SetIndexingInfo(analyzerTypes, indexFieldTypes, sortFieldNames);
        }

        public QueryResult<int> ExecuteQuery(SnQuery query)
        {
            return Channel.ExecuteQuery(query);
        }

        public QueryResult<string> ExecuteQueryAndProject(SnQuery query)
        {
            return Channel.ExecuteQueryAndProject(query);
        }
    }
}
