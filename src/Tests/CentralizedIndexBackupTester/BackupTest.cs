﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Diagnostics;
using SenseNet.Search.Lucene29;

namespace CentralizedIndexBackupTester
{
    public interface IWorker
    {
        Task WorkAsync(CancellationToken cancellationToken);
    }

    public abstract class BackupTest
    {
        protected readonly ILuceneIndexingEngine _engine;
        protected readonly string _backupDirectoryPath; // ...this...\bin\Debug\netcoreapp3.1\App_Data\IndexBackup

        public BackupTest(ILuceneIndexingEngine engine)
        {
            _engine = engine;
            _backupDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexBackup");
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Test.StartOperation("ContinuousIndexTest"))
            {
                // Start an editor worker agent
                var finisher = new CancellationTokenSource();
                var _ = CreateWorker().WorkAsync(finisher.Token).ConfigureAwait(false);

                // Wait for backup
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                // Do backup
                await BackupAsync(cancellationToken).ConfigureAwait(false);

                // Wait for finish
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                // Stop worker agent
                finisher.Cancel();

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                op.Successful = true;
            }
        }

        protected abstract IWorker CreateWorker();

        private async Task BackupAsync(CancellationToken cancellationToken)
        {
            var status = await IndexManager.LoadCurrentIndexingActivityStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            using (var op = SnTrace.StartOperation("#### BACKUP " + this.GetType().Name))
            {
                SnTrace.Write("####   Indexing activity status: " + status);
                Console.WriteLine("BACKUP start. Indexing activity status: " + status);

                await _engine.BackupAsync(_backupDirectoryPath, CancellationToken.None)
                    .ConfigureAwait(false);

                Console.WriteLine("BACKUP finished");
                op.Successful = true;
            }
        }

    }
}