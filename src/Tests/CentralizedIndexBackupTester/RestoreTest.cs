using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;

namespace CentralizedIndexBackupTester
{
    /// <summary>
    /// After execution of the "Continuous index test" copy the index from the backup
    /// to the given index directory to simulate the restore operation.
    /// </summary>
    public class RestoreTest
    {
        private readonly ILuceneIndexingEngine _engine;
        private readonly string _backupDirectoryPath; // ...this...\bin\Debug\netcoreapp3.1\App_Data\IndexBackup

        public RestoreTest(ILuceneIndexingEngine engine)
        {
            _engine = engine;
            _backupDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexBackup");
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Test.StartOperation("RestoreTest"))
            {
                var dbStatus = await DataStore.LoadCurrentIndexingActivityStatusAsync(cancellationToken)
                    .ConfigureAwait(false);

                var indexStatus = await _engine.ReadActivityStatusFromIndexAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
