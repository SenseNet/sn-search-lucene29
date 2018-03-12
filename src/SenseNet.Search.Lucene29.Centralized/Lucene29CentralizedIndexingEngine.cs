using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lucene.Net.Analysis;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    public class Lucene29CentralizedIndexingEngine : ILuceneIndexingEngine
    {
        //UNDONE: [INDEX] implement IndexingEngine.Running?
        public bool Running
        {
            get { return true; }
            // ReSharper disable once ValueParameterNotUsed
            set
            {
                //do nothing
            }
        } 
        public bool IndexIsCentralized => true;
        public LuceneSearchManager LuceneSearchManager => throw new NotImplementedException(); // this is not necessary in a centralized environment

        public void Start(TextWriter consoleOut)
        {
            //UNDONE: make sure the search service is accessible
        }

        public void ShutDown()
        {
            //UNDONE: [INDEX] maybe write the activity status here
        }

        public void ClearIndex()
        {
            SearchServiceClient.Instance.ClearIndex();
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            return SearchServiceClient.Instance.ReadActivityStatusFromIndex();
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            SearchServiceClient.Instance.WriteActivityStatusToIndex(state);
        }

        public void WriteIndex(IEnumerable<SnTerm> deletions, IEnumerable<DocumentUpdate> updates, IEnumerable<IndexDocument> additions)
        {
            SearchServiceClient.Instance.WriteIndex(deletions, updates, additions);
        }
        
        public void SetIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> indexingInfo)
        {
            //UNDONE: make sure this works correctly through the wire
            var analyzers = indexingInfo.Where(kvp => kvp.Value.Analyzer != IndexFieldAnalyzer.Default).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Analyzer);
            var indexFieldTypes = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.IndexFieldType);

            SearchServiceClient.Instance.SetIndexingInfo(analyzers, indexFieldTypes);
        }

        public Analyzer GetAnalyzer()
        {
            // this is not needed on the web server in a centralized environment
            throw new NotImplementedException();
        }
    }
}
