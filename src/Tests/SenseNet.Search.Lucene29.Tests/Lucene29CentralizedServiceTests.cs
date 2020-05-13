using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Index;
using SenseNet.Tests;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29CentralizedServiceTests : TestBase
    {
        private class TestBackupManagerFactory : IBackupManagerFactory
        {
            public bool FinishRequested { get; set; }
            public IBackupManager CreateBackupManager()
            {
                return new TestBackupManager(this);
            }

        }
        private class TestBackupManager : IBackupManager
        {
            private readonly TestBackupManagerFactory _factory;
            public TestBackupManager(TestBackupManagerFactory factory)
            {
                // Memorize the finish-requester
                _factory = factory;
            }

            public async Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                // Block the task until the stop request.
                while (!_factory.FinishRequested)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }

        [TestMethod]
        public void L29_Service_OnlyOneBackup()
        {
            var factory = new TestBackupManagerFactory();
            var service = new SearchService {BackupManagerFactory = factory};

            // First call runs async way.
            Task.Run(() => { service.Backup(null, null); });
            Thread.Sleep(200);

            try
            {
                // The second (sync) call needs to be refused.
                service.Backup(null, null);
                Assert.Fail("Expected exception was not thrown: BackupAlreadyExecutingException");
            }
            catch (BackupAlreadyExecutingException)
            {
            }

            // Stop the blocking call.
            factory.FinishRequested = true;

            Task.Delay(200).GetAwaiter().GetResult();
        }
    }
}
