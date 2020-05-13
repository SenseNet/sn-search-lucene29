using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.Index;
using SenseNet.Tests;

namespace SenseNet.Search.Lucene29.Tests
{
    [TestClass]
    public class Lucene29CentralizedServiceTests : TestBase
    {
        private class TestBackupManager : IBackupManager, IBackupManagerFactory
        {
            public IBackupManager CreateBackupManager()
            {
                return new TestBackupManager();
            }
            public async Task BackupAsync(IndexingActivityStatus state, string backupDirectoryPath,
                LuceneSearchManager indexManager, CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        [TestMethod]
        public void L29_Service_OnlyOneBackup()
        {
            var service = new SearchService { BackupManagerFactory = new TestBackupManager() };

            var tasks = Enumerable.Range(0, 5)
                .Select(x => Task.Run(() => service.Backup(null, null)))
                .ToArray();
            // ReSharper disable once CoVariantArrayConversion
            Task.WaitAll(tasks);

            var completed = new string(tasks.Select(t => t.Result == IndexBackupResult.Finished ? 'Y' : 'n').ToArray());
            var faulted = new string(tasks.Select(t => t.IsFaulted ? 'Y' : 'n').ToArray());

            Assert.AreEqual("Ynnnn", completed);
            Assert.AreEqual("nnnnn", faulted);
        }

    }
}
