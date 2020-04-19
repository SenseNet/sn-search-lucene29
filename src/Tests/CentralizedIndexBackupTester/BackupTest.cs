using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Search.Indexing.Activities;
using SenseNet.ContentRepository.Storage;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using Task = System.Threading.Tasks.Task;

namespace CentralizedIndexBackupTester
{
    public class BackupTest
    {
        private readonly TimeSpan _wait1Sec = TimeSpan.FromSeconds(1);
        private readonly TimeSpan _wait3Sec = TimeSpan.FromSeconds(3);
        private readonly TimeSpan _wait5Sec = TimeSpan.FromSeconds(5);

        private readonly string _backupDirectoryPath;
        private readonly ILuceneIndexingEngine _engine;

        // D:\projects\_fromTemplate\IndexBackup\IndexBackupTester\bin\Debug\netcoreapp3.1\App_Data\IndexBackup
        public BackupTest(ILuceneIndexingEngine engine)
        {
            _engine = engine;
            _backupDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexBackup");
        }

        //private static readonly string TestRootName = "IndexBackupTest";
        //private static readonly string TestRootPath = "/Root/IndexBackupTest";
        public async Task Run(CancellationToken cancellationToken)
        {
            //// Ensure test folder
            //var testRoot = await Node.LoadAsync<SystemFolder>(TestRootPath, cancellationToken);
            //if (testRoot == null)
            //{
            //    testRoot = new SystemFolder(Repository.Root) { Name = TestRootName };
            //    testRoot.Save();
            //}

            // Start an editor worker agent
            var finisher = new CancellationTokenSource();
            var editorTask = Work(finisher.Token);

            // Wait for backup
            await Task.Delay(_wait5Sec, cancellationToken);

            // Do backup
            await Backup();

            // Wait for finish
            await Task.Delay(_wait5Sec, cancellationToken);

            // Stop worker agent
            finisher.Cancel();

            // Check test result
            Check();
        }

        private async Task Work(CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(_wait1Sec, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Work finished");
                    return;
                }
                Console.WriteLine("Work");
            }
        }

        private Task Backup()
        {
            //IndexManager.LoadCurrentIndexingActivityStatus();

            Console.WriteLine("Backup start");
            var timer = Stopwatch.StartNew();

            _engine.BackupAsync(_backupDirectoryPath, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            timer.Stop();
            Console.WriteLine("Backup finished. Elapsed time: " + timer.Elapsed);

            return Task.CompletedTask;
        }

        private void Check()
        {
            Console.WriteLine("Check result");
        }

    }
}
