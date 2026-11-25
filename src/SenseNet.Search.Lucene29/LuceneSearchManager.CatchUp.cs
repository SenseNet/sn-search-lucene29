using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Index;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29
{
    public partial class LuceneSearchManager
    {
        private readonly ReaderWriterLockSlim _catchUpLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private CatchUpSession _catchUp;

        public IIndexCatchUpSession BeginCatchUp(CancellationToken cancellationToken)
        {
            _catchUpLock.EnterWriteLock();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Running || _catchUp != null)
                    throw new InvalidOperationException("The index writer is not available for catch-up.");

                // Preserve pending normal writes as the rollback baseline without advancing their checkpoint.
                _writer.Commit(IndexReader.GetCommitUserData(_writer.GetDirectory()));
                return _catchUp = new CatchUpSession(this);
            }
            finally
            {
                _catchUpLock.ExitWriteLock();
            }
        }

        private void UseIndexWriter(Action action, CatchUpSession session = null)
        {
            _catchUpLock.EnterReadLock();
            try
            {
                if (_catchUp != session)
                    throw new InvalidOperationException("The index writer is not available for this operation during catch-up.");
                action();
            }
            finally
            {
                _catchUpLock.ExitReadLock();
            }
        }

        private void RollbackCatchUp()
        {
            var writer = _writer;
            var reader = _reader;
            var session = _catchUp;
            _writer = null;
            _reader = null;
            _catchUp = null;
            Running = false;
            try
            {
                session.CloseReader();
            }
            finally
            {
                try
                {
                    reader.Close();
                }
                finally
                {
                    writer.Rollback();
                }
            }
        }

        private sealed class CatchUpSession : IIndexCatchUpSession
        {
            private readonly LuceneSearchManager _manager;
            private readonly object _sync = new object();
            private IndexReader _reader;

            public CatchUpSession(LuceneSearchManager manager)
            {
                _manager = manager;
            }

            public IReadOnlyList<IndexCatchUpVersion> GetVersions(SnTerm term, CancellationToken cancellationToken)
            {
                var result = new List<IndexCatchUpVersion>();
                _manager.UseIndexWriter(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_sync)
                    {
                        _reader ??= _manager._writer.GetReader();
                        var postings = _reader.TermDocs(_manager.GetTerm(term));
                        try
                        {
                            while (postings.Next())
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var document = _reader.Document(postings.Doc());
                                result.Add(new IndexCatchUpVersion(
                                    int.Parse(document.Get(IndexFieldName.VersionId), CultureInfo.InvariantCulture),
                                    document.Get(IndexFieldName.IsLastPublic) == IndexValue.Yes,
                                    document.Get(IndexFieldName.IsLastDraft) == IndexValue.Yes));
                            }
                        }
                        finally
                        {
                            postings.Close();
                        }
                    }
                }, this);
                return result;
            }

            public Task WriteIndexAsync(IReadOnlyList<SnTerm> deletions, IReadOnlyList<DocumentUpdate> updates,
                CancellationToken cancellationToken)
            {
                _manager.UseIndexWriter(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_sync)
                    {
                        CloseReader();
                        _manager.WriteIndexCore(deletions, updates, null);
                    }
                }, this);
                return Task.CompletedTask;
            }

            public Task CommitAsync(IndexingActivityStatus status, CancellationToken cancellationToken)
            {
                _manager._catchUpLock.EnterWriteLock();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_manager._catchUp != this)
                        throw new InvalidOperationException("The catch-up session is closed.");
                    CloseReader();
                    _manager.Commit(false, status);
                    _manager._catchUp = null;

                    try
                    {
                        _manager.ReopenReader();
                    }
                    catch (Exception error)
                    {
                        _manager.IndexReopenedAt = default;
                        SnLog.WriteException(error, "Catch-up was committed. Lucene reader refresh will be retried on the next read.");
                    }

                    return Task.CompletedTask;
                }
                finally
                {
                    _manager._catchUpLock.ExitWriteLock();
                }
            }

            public void Dispose()
            {
                _manager._catchUpLock.EnterWriteLock();
                try
                {
                    if (_manager._catchUp == this)
                        _manager.RollbackCatchUp();
                }
                finally
                {
                    _manager._catchUpLock.ExitWriteLock();
                }
            }

            internal void CloseReader()
            {
                var reader = _reader;
                _reader = null;
                reader?.Close();
            }
        }
    }
}
