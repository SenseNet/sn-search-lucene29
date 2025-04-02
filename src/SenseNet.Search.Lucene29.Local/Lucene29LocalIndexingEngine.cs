using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Search;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    /// <summary>
    /// Lucene29 indexing engine for a local environment. Works with a Lucene index stored in the file system.
    /// </summary>
    /// <inheritdoc/>
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
            var indexDir = indexDirectory ?? new IndexDirectory(null, Providers.Instance.SearchManager.IndexDirectoryPath);

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
        internal async Task StartAsync(TextWriter consoleOut, bool warmup, CancellationToken cancellationToken)
        {
            LuceneSearchManager.Start(consoleOut);

            if(warmup)
                await SnQuery.QueryAsync("+Id:1", SnQueryContext.CreateDefault(), cancellationToken);
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

        /* ================================================================================================= */

        /// <inheritdoc />
        public async Task<BackupResponse> BackupAsync(string target, CancellationToken cancellationToken)
        {
            using var op = SnTrace.System.StartOperation($"Index backup. Lucene29CentralizedIndexingEngine");

            // Activity state update is not necessary in the local index (only in the centralized).
            var result = ___Backup(null, target);
            op.Successful = true;
            return result;
        }

        private BackupResponse ___Backup(IndexingActivityStatus state, string backupDirectoryPath)
        {
            if (_backupManager != null)
            {
                SnTrace.Index.Write("LocalIndexingEngine: Backup already executing by another thread.");
                return CreateBackupResponse(BackupState.Executing, false);
            }

            lock (_backupLock)
            {
                if (_backupManager != null)
                    return CreateBackupResponse(BackupState.Executing, false);
                _backupManager = _backupManagerFactory.CreateBackupManager();
            }

            if(!_backupManager.CheckDirectory(backupDirectoryPath))
            {
                var message = "LocalIndexingEngine: Backup directory is not empty.";
                SnTrace.Index.WriteError(message + " Path: " + backupDirectoryPath);
                var result = CreateBackupResponse(BackupState.Faulted, false);
                var info = result.Current;
                info.StartedAt = DateTime.UtcNow;
                info.FinishedAt = DateTime.UtcNow;
                info.TargetPath = backupDirectoryPath;
                info.Message = message;
                _backupHistory.Add(info.Clone());
                _backupManager = null;
                return result;
            }

            SnTrace.Index.Write("LocalIndexingEngine: BackupManager created.");
            Task.Run(() => BackupWorker(state, backupDirectoryPath));

            return CreateBackupResponse(BackupState.Started, false);
        }
        private IBackupManagerFactory _backupManagerFactory { get; set; } = new BackupManager();
        private readonly object _backupLock = new object();
        private IBackupManager _backupManager;
        private CancellationTokenSource _backupCancellationSource;
        private readonly List<BackupInfo> _backupHistory = new List<BackupInfo>();
        private void BackupWorker(IndexingActivityStatus state, string backupDirectoryPath)
        {
            try
            {
                _backupCancellationSource = new CancellationTokenSource();
                _backupManager.Backup(state, backupDirectoryPath, LuceneSearchManager,
                    _backupCancellationSource.Token);
            }
            catch (Exception e)
            {
                CollectErrorMessages(e, _backupManager.BackupInfo);
                SnTrace.Index.WriteError("LocalIndexingEngine: " + _backupManager.BackupInfo.Message);
            }

            _backupHistory.Add(_backupManager.BackupInfo.Clone());
            SnTrace.Index.Write("LocalIndexingEngine: BackupInfo is added to history.");
            _backupManager = null;
            _backupCancellationSource.Dispose();
            _backupCancellationSource = null;
        }
        private void CollectErrorMessages(Exception exception, BackupInfo targetInfo)
        {
            var sb = new StringBuilder(exception is TaskCanceledException ? "CANCELED: " : "ERROR: ");
            CollectErrorMessages(exception, sb, "");
            targetInfo.Message = sb.ToString();
        }
        private void CollectErrorMessages(Exception exception, StringBuilder sb, string indent)
        {
            sb.Append(indent);
            sb.Append(exception.GetType().FullName).Append(": ");
            sb.AppendLine(exception.Message);
            if (exception is AggregateException ae)
            {
                var indent2 = indent + "  ";
                foreach (var ex in ae.InnerExceptions)
                    CollectErrorMessages(ex, sb, indent2);
            }
            if (exception.InnerException != null)
                CollectErrorMessages(exception.InnerException, sb, indent + "  ");
        }


        /// <inheritdoc />
        public Task<BackupResponse> QueryBackupAsync(CancellationToken cancellationToken)
        {
            BackupState state;
            if (_backupManager != null)
            {
                state = BackupState.Executing;
            }
            else
            {
                BackupInfo info = _backupHistory.LastOrDefault();
                if (info == null)
                {
                    state = BackupState.Initial;
                }
                else
                {
                    if (info.Message != null)
                    {
                        if(info.Message == "Canceled")
                            state = BackupState.Canceled;
                        else if(info.Message == "Finished")
                            state = BackupState.Finished;
                        else
                            state = BackupState.Faulted;
                    }
                    else
                    {
                        state = BackupState.Finished;
                    }
                }
            }

            return Task.FromResult(CreateBackupResponse(state, true));
        }

        /// <inheritdoc />
        public Task<BackupResponse> CancelBackupAsync(CancellationToken cancellationToken)
        {
            _backupCancellationSource?.Cancel();
            return Task.FromResult(CreateBackupResponse(BackupState.CancelRequested, true));
        }

        private BackupResponse CreateBackupResponse(BackupState state, bool withHistory)
        {
            return new BackupResponse
            {
                State = state,
                Current = _backupManager?.BackupInfo.Clone(),
                History = withHistory ? _backupHistory.OrderByDescending(x => x.StartedAt).ToArray() : null,
            };
        }

        /* ================================================================================================= */

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

        /// <inheritdoc />
        public IndexProperties GetIndexProperties()
        {
            return new IndexExplorer(LuceneSearchManager).GetIndexProperties();
        }
        /// <inheritdoc />
        public async Task<IDictionary<string, IDictionary<string, List<int>>>> GetInvertedIndexAsync(CancellationToken cancel)
        {
            return await new IndexExplorer(LuceneSearchManager).GetInvertedIndexAsync(cancel);
        }
        /// <inheritdoc />
        public async Task<IDictionary<string, List<int>>> GetInvertedIndexAsync(string fieldName, CancellationToken cancel)
        {
            return await new IndexExplorer(LuceneSearchManager).GetInvertedIndexAsync(fieldName, cancel);
        }
        /// <inheritdoc />
        public IDictionary<string, string> GetIndexDocumentByVersionId(int versionId)
        {
            return new IndexExplorer(LuceneSearchManager).GetIndexDocumentByVersionId(versionId);
        }
        /// <inheritdoc />
        public IDictionary<string, string> GetIndexDocumentByDocumentId(int documentId)
        {
            return new IndexExplorer(LuceneSearchManager).GetIndexDocumentByDocumentId(documentId);
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
            return ((Lucene29LocalIndexingEngine)Providers.Instance.IndexManager.IndexingEngine).GetIndexReaderFrame(dirty);
        }

        //===================================================================================== ILuceneIndexingEngine implementation

        /// <inheritdoc />
        public Analyzer GetAnalyzer()
        {
            return LuceneSearchManager.GetAnalyzer();
        }

        public IDictionary<string, string> GetConfigurationInfo()
        {
            return new Dictionary<string, string>
            {
                {"IndexIsCentralized", "false"},
                {"IndexDirectory", IndexDirectory.CurrentDirectory}
            };
        }

        public IDictionary<string, string> GetHealth()
        {
            throw new NotImplementedException();
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
