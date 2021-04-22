using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    /// <summary>
    /// Lucene29 indexing engine for a local environment. Works with a Lucene index stored in the file system.
    /// </summary>
    public class Lucene29LocalIndexingEngine : ILuceneIndexingEngine
    {
        internal IndexDirectory IndexDirectory => LuceneSearchManager.IndexDirectory;

        /// <summary>
        /// Gets the Lucene search manager instance that is responsible for indexing operations.
        /// </summary>
        public LuceneSearchManager LuceneSearchManager { get; }

        //===================================================================================== Constructors

        /// <summary>
        /// Initializes an instance of the Lucene29LocalIndexingEngine class. Needed for automatic type loading.
        /// </summary>
        public Lucene29LocalIndexingEngine() : this(null)
        {
            // default constructor is needed for automatic type loading
        }
        /// <summary>
        /// Initializes an instance of the Lucene29LocalIndexingEngine class.
        /// </summary>
        /// <param name="indexDirectory">File system directory for storing the index. 
        /// If not provided, <see cref="SearchManager.IndexDirectoryPath"/> will be used.</param>
        public Lucene29LocalIndexingEngine(IndexDirectory indexDirectory)
        {
            var indexDir = indexDirectory ?? new IndexDirectory(null, SearchManager.IndexDirectoryPath);

            LuceneSearchManager = new LuceneSearchManager(indexDir, Notification.NotificationSender); 

            SetEventhandlers();
        }

        private void SetEventhandlers()
        {
            // set up event handlers
            LuceneSearchManager.OnStarted += Startup;
            LuceneSearchManager.OnLockFileRemoved += StartMessaging;
        }

        //===================================================================================== IIndexingEngine implementation

        /// <summary>
        /// Returns false, because this is a local indexing engine.
        /// </summary>
        public bool IndexIsCentralized => false;
        /// <summary>
        /// Gets a value indicating whether the underlying Lucene search manager is running.
        /// </summary>
        public bool Running
        {
            get => LuceneSearchManager.Running;
            internal set => LuceneSearchManager.Running = value;
        }

        /// <inheritdoc />
        /// <summary>
        /// Starts the underlying Lucene search manager.
        /// </summary>
        public Task StartAsync(TextWriter consoleOut, CancellationToken cancellationToken)
        {
            return StartAsync(consoleOut, true, cancellationToken);
        }

        /// <summary>
        /// Entrance for tests only.
        /// </summary>
        internal Task StartAsync(TextWriter consoleOut, bool warmup, CancellationToken cancellationToken)
        {
            LuceneSearchManager.Start(consoleOut);

            if(warmup)
                SnQuery.Query("+Id:1", SnQueryContext.CreateDefault());

            return Task.CompletedTask;
        }

        /// <summary>
        /// Derived classes may add custom logic here that will be executed at the end
        /// of the start process, but before the Running switch is set to True.
        /// </summary>
        /// <param name="consoleOut"></param>
        protected virtual void Startup(TextWriter consoleOut) { }

        /// <inheritdoc />
        /// <summary>
        /// Stops the underlying Lucene search manager.
        /// </summary>
        public Task ShutDownAsync(CancellationToken cancellationToken)
        {
            //TODO: CommitState: maybe need to write the final state in the distributed environment.
            // IndexManager.GetCurrentIndexingActivityStatus()
            // WriteActivityStatusToIndex
            LuceneSearchManager.ShutDown();

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <summary>
        /// This method is not supported.
        /// </summary>
        public Task<BackupResponse> BackupAsync(string target, CancellationToken cancellationToken)
        {
            throw new SnNotSupportedException();
        }

        /// <inheritdoc />
        /// <summary>
        /// This method is not supported.
        /// </summary>
        public Task<BackupResponse> QueryBackupAsync(CancellationToken cancellationToken)
        {
            throw new SnNotSupportedException();
        }

        /// <inheritdoc />
        /// <summary>
        /// This method is not supported.
        /// </summary>
        public Task<BackupResponse> CancelBackupAsync(CancellationToken cancellationToken)
        {
            throw new SnNotSupportedException();
        }

        /// <inheritdoc />
        public Task ClearIndexAsync(CancellationToken cancellationToken)
        {
            LuceneSearchManager.ClearIndex();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IndexingActivityStatus> ReadActivityStatusFromIndexAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LuceneSearchManager.ReadActivityStatusFromIndex());
        }

        /// <inheritdoc />
        public Task WriteActivityStatusToIndexAsync(IndexingActivityStatus state, CancellationToken cancellationToken)
        {
            LuceneSearchManager.WriteActivityStatusToIndex(state);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task WriteIndexAsync(IEnumerable<SnTerm> deletions, IEnumerable<DocumentUpdate> updates, IEnumerable<IndexDocument> additions, CancellationToken cancellationToken)
        {
            LuceneSearchManager.WriteIndex(deletions, updates, additions);
            return Task.CompletedTask;
        }

        //===================================================================================== IndexReader

        private IndexReaderFrame GetIndexReaderFrame(bool dirty)
        {
            return LuceneSearchManager.GetIndexReaderFrame(dirty);
        }
        /// <summary>
        /// Gets an <see cref="IndexReaderFrame"/> from the indexing engine.
        /// </summary>
        /// <param name="dirty">Whether the reader should be reopened from the writer. Default is false.</param>
        public static IndexReaderFrame GetReaderFrame(bool dirty = false)
        {
            return ((Lucene29LocalIndexingEngine)IndexManager.IndexingEngine).GetIndexReaderFrame(dirty);
        }

        //===================================================================================== ILuceneIndexingEngine implementation

        /// <inheritdoc />
        public Analyzer GetAnalyzer()
        {
            return LuceneSearchManager.GetAnalyzer();
        }

        /// <inheritdoc />
        public void SetIndexingInfo(IDictionary<string, IPerFieldIndexingInfo> indexingInfo)
        {
            var analyzers = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => GetAnalyzer(kvp.Value));
            var indexFieldTypes = indexingInfo.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.IndexFieldHandler.IndexFieldType);

            LuceneSearchManager.SetIndexingInfo(analyzers, indexFieldTypes);
        }

        //===================================================================================== Helper methods

        internal static Analyzer GetAnalyzer(IPerFieldIndexingInfo pfii)
        {
            var analyzerToken = pfii.Analyzer == IndexFieldAnalyzer.Default
                ? pfii.IndexFieldHandler.GetDefaultAnalyzer()
                : pfii.Analyzer;

            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (analyzerToken)
            {
                case IndexFieldAnalyzer.Keyword: return new KeywordAnalyzer();
                case IndexFieldAnalyzer.Standard: return new StandardAnalyzer(LuceneSearchManager.LuceneVersion);
                case IndexFieldAnalyzer.Whitespace: return new WhitespaceAnalyzer();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void StartMessaging()
        {
        }
    }
}
