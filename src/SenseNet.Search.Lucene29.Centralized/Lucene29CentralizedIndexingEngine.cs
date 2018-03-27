using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using Lucene.Net.Analysis;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Querying;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29
{
    /// <summary>
    /// Centralized indexing engine built using Lucene 2.9.
    /// It requires a central search service accessible for all web servers at all times. All operations
    /// are routed towards that service with a short retry period.
    /// </summary>
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
            // warmup
            var unused = SearchServiceClient.Instance;

            _running = true;
        }

        public void ShutDown()
        {
            //TODO: we may write the indexing state (last activity id) to the index in the future
            // to make the centralized index compatible with the local version. Currently the state
            // is not written there because it is not needed for a centralized index to work.
        }

        public void ClearIndex()
        {
            Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.ClearIndex());
        }

        public IndexingActivityStatus ReadActivityStatusFromIndex()
        {
            return Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.ReadActivityStatusFromIndex());
        }

        public void WriteActivityStatusToIndex(IndexingActivityStatus state)
        {
            //TODO: we may write the indexing state (last activity id) to the index in the future
            // to make the centralized index compatible with the local version. Currently the state
            // is not written there because it is not needed for a centralized index to work.

            Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.WriteActivityStatusToIndex(state));
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
                    RetryWrite(partition.ToArray(), write);

                    partition.Clear();
                }

                // process the last page
                if (partition.Any())
                    RetryWrite(partition.ToArray(), write);
            }

            void RetryWrite<T>(T[] data, Action<T[]> write)
            {
                Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds,
                    () => write(data), (remainingCount, ex) =>
                    {
                        if (ex == null)
                            return true;
                        if (remainingCount == 1)
                            throw ex;

                        SnTrace.Index.WriteError($"WriteIndex: {ex.Message} Remaining retry count: {remainingCount - 1}");

                        return true;
                    });
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

            Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.SetIndexingInfo(analyzers, indexFieldTypes, sortInfo));
        }

        public Analyzer GetAnalyzer()
        {
            // this is not needed on the web server in a centralized environment
            throw new NotImplementedException();
        }
    }
}
