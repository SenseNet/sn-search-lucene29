using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
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

        public virtual async Task RunAsync(CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Test.StartOperation(this.GetType().Name))
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

        protected async Task BackupAsync(CancellationToken cancellationToken)
        {
            var status = await IndexManager.LoadCurrentIndexingActivityStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            using (var op = SnTrace.StartOperation("#### BACKUP " + this.GetType().Name))
            {
                SnTrace.Write("####   Indexing activity status: " + status);
                Console.WriteLine();
                Console.WriteLine("BACKUP start. Indexing activity status: " + status);

                var response = await _engine.BackupAsync(_backupDirectoryPath, CancellationToken.None)
                    .ConfigureAwait(false);

                if (response.State == BackupState.Started)
                {
                    while (true)
                    {
                        await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                        response = await _engine.QueryBackupAsync(cancellationToken).ConfigureAwait(false);
                        WriteProgress(response);

                        if (response.State != BackupState.Executing)
                            break;
                    }
                    //Console.WriteLine("BACKUP finished");
                }
                else
                {
                    Console.WriteLine("BACKUP already started");
                }

                op.Successful = true;
            }
        }

        private void WriteProgress(BackupResponse response)
        {
            var info = response.Current ?? response.History?.FirstOrDefault();

            if(info == null)
                Console.Write("\t\tBACKUP: {0}, bytes: ?/?, files: ?/?, ?                  \r",
                    response.State);
            else
                Console.Write("\t\tBACKUP: {0}, bytes: {1}/{2}, files: {3}/{4}, {5}                \r",
                    response.State,
                    info.CopiedBytes, info.TotalBytes,
                    info.CopiedFiles, info.CountOfFiles,
                    info.CurrentlyCopiedFile);
        }
    }
}
