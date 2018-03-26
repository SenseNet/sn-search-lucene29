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
        private static readonly int ServiceWritePartitionSize = 50;
        private bool _running;

        public bool Running
        {
            get => _running;
            set
            {
                if (!value)
                    throw new NotSupportedException("Switching off a centralized indexing engine is not supported.");
            }
        } 
        public bool IndexIsCentralized => true;
        public LuceneSearchManager LuceneSearchManager => throw new NotImplementedException(); // this is not necessary in a centralized environment

        public void Start(TextWriter consoleOut)
        {
            //UNDONE: make sure the search service is accessible
            _running = true;
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
            // local function for partitioning index document collections
            void WriteIndex<T>(IEnumerable<T> source, Action<T[]> write)
            {
                if (source == null)
                    return;

                var partition = new List<T>(ServiceWritePartitionSize);

                // enumerate the source collection only once
                foreach (var item in source)
                {
                    // fill the buffer with items to send
                    partition.Add(item);
                    if (partition.Count < ServiceWritePartitionSize)
                        continue;

                    // send a bunch of data to the service and clean the buffer
                    write(partition.ToArray());
                    partition.Clear();
                }

                // process the last page
                if (partition.Any())
                    write(partition.ToArray());
            }

            WriteIndex(deletions, deleteTerms => SearchServiceClient.Instance.WriteIndex(deleteTerms, null, null));
            WriteIndex(updates, updateDocuments => SearchServiceClient.Instance.WriteIndex(null, updateDocuments, null));
            WriteIndex(additions, indexDocuments => SearchServiceClient.Instance.WriteIndex(null, null, indexDocuments));
        }
        
        public void SetIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> indexingInfo)
        {
            var analyzers = indexingInfo.Where(kvp => kvp.Value.Analyzer != IndexFieldAnalyzer.Default).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Analyzer);
            var indexFieldTypes = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.IndexFieldType);
            var sortInfo = indexingInfo
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.GetSortFieldName(kvp.Key))
                .Where(kvp => string.CompareOrdinal(kvp.Key, kvp.Value) != 0).ToDictionary(p => p.Key, p => p.Value);

            SearchServiceClient.Instance.SetIndexingInfo(analyzers, indexFieldTypes, sortInfo);
        }

        public Analyzer GetAnalyzer()
        {
            // this is not needed on the web server in a centralized environment
            throw new NotImplementedException();
        }
    }
}
