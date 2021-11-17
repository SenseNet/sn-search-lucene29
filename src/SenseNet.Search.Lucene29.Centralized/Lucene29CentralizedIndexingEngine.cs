using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Microsoft.Extensions.Options;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
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
        private readonly CentralizedOptions _centralizedOptions;

        public Lucene29CentralizedIndexingEngine(ISearchServiceClient client, IOptions<CentralizedOptions> options)
        {
            //TODO: legacy behavior: remove the pinned instance and create a factory service
            // or a transient service that can do the same thing: provide a new instance every time.

            // initialize the legacy instance in a DI environment
            if (client != null)
                SearchServiceClient.Instance = client;

            _centralizedOptions = options.Value;
        }

        public Task StartAsync(TextWriter consoleOut, CancellationToken cancellationToken)
        {
            // start the underlying client
            var client = SearchServiceClient.Instance;
            if (client == null)
                throw new InvalidOperationException("Service client is unavailable, service cannot be started.");

            client.Start();

            _running = true;
            return Task.CompletedTask;
        }

        public Task ShutDownAsync(CancellationToken cancellationToken)
        {
            //TODO: we may write the indexing state (last activity id) to the index in the future
            // to make the centralized index compatible with the local version. Currently the state
            // is not written there because it is not needed for a centralized index to work.

            SearchServiceClient.Instance?.ShutDown();
            _running = false;

            return Task.CompletedTask;
        }

        public async Task<BackupResponse> BackupAsync(string target, CancellationToken cancellationToken)
        {
            BackupResponse result;
            using (var op = SnTrace.System.StartOperation($"Index backup. Lucene29CentralizedIndexingEngine"))
            {
                if (target == null)
                {
                    SnTrace.System.WriteError($"Index backup error: target is not specified.");
                    throw new ArgumentNullException(nameof(target));
                }

                await IndexManager.DeleteRestorePointsAsync(cancellationToken).ConfigureAwait(false);

                var state = await IndexManager.LoadCurrentIndexingActivityStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
                SnTrace.System.Write($"Index backup indexing-activity status: {state}");

                result = SearchServiceClient.Instance.Backup(state, target);

                op.Successful = true;
            }

            return result;
        }

        public Task<BackupResponse> QueryBackupAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SearchServiceClient.Instance.QueryBackup());
        }

        public Task<BackupResponse> CancelBackupAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SearchServiceClient.Instance.CancelBackup());
        }

        public Task ClearIndexAsync(CancellationToken cancellationToken)
        {
            Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.ClearIndex());

            return Task.CompletedTask;
        }

        public Task<IndexingActivityStatus> ReadActivityStatusFromIndexAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Retrier.Retry(SearchServiceClient.RetryCount,
                SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.ReadActivityStatusFromIndex()));
        }

        public Task WriteActivityStatusToIndexAsync(IndexingActivityStatus state, CancellationToken cancellationToken)
        {
            //TODO: we may write the indexing state (last activity id) to the index in the future
            // to make the centralized index compatible with the local version. Currently the state
            // is not written there because it is not needed for a centralized index to work.

            Retrier.Retry(SearchServiceClient.RetryCount, SearchServiceClient.RetryWaitMilliseconds, typeof(CommunicationException),
                () => SearchServiceClient.Instance.WriteActivityStatusToIndex(state));

            return Task.CompletedTask;
        }
        
        public Task WriteIndexAsync(IEnumerable<SnTerm> deletions, IEnumerable<DocumentUpdate> updates, IEnumerable<IndexDocument> additions, CancellationToken cancellationToken)
        {
            // local function for partitioning index document collections
            void WriteIndex<T>(IEnumerable<T> source, Action<T[]> write)
            {
                if (source == null)
                    return;

                var partition = new List<T>(_centralizedOptions.ServiceWritePartitionSize);

                // enumerate the source collection only once
                foreach (var item in source)
                {
                    // fill the buffer with items to send
                    partition.Add(item);
                    if (partition.Count < _centralizedOptions.ServiceWritePartitionSize)
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

            //UNDONE: [async] make this async
            WriteIndex(deletions, deleteTerms => SearchServiceClient.Instance.WriteIndex(deleteTerms, null, null));
            WriteIndex(updates, updateDocuments => SearchServiceClient.Instance.WriteIndex(null, updateDocuments, null));
            WriteIndex(additions, indexDocuments => SearchServiceClient.Instance.WriteIndex(null, null, indexDocuments));

            return Task.CompletedTask;
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
