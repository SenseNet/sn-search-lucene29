using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Search.Lucene29;

namespace CentralizedIndexBackupTester
{
    public class BackupTest
    {
        private readonly TimeSpan _wait1Sec = TimeSpan.FromSeconds(1);
        private readonly TimeSpan _wait3Sec = TimeSpan.FromSeconds(3);
        private readonly TimeSpan _wait5Sec = TimeSpan.FromSeconds(5);

        private readonly ILuceneIndexingEngine _engine;
        private readonly string _backupDirectoryPath; // ...this...\bin\Debug\netcoreapp3.1\App_Data\IndexBackup

        public BackupTest(ILuceneIndexingEngine engine)
        {
            _engine = engine;
            _backupDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexBackup");
        }

        public async Task Run(CancellationToken cancellationToken)
        {
            // Start an editor worker agent
            var finisher = new CancellationTokenSource();
            var _ = new Worker().Work(finisher.Token);

            // Wait for backup
            await Task.Delay(_wait5Sec, cancellationToken);

            // Do backup
            await Backup();

            // Wait for finish
            await Task.Delay(_wait5Sec, cancellationToken);

            // Stop worker agent
            finisher.Cancel();
        }

        private Task Backup()
        {
            Console.WriteLine("Backup start");
            Console.WriteLine("  Indexing activity status: " + IndexManager.LoadCurrentIndexingActivityStatus());

            var timer = Stopwatch.StartNew();

            _engine.BackupAsync(_backupDirectoryPath, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            timer.Stop();
            Console.WriteLine("Backup finished. Elapsed time: " + timer.Elapsed);

            return Task.CompletedTask;
        }
    }
}
