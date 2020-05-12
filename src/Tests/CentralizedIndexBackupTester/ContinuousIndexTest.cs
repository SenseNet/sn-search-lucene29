using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.Diagnostics;
using SenseNet.Search.Lucene29;

namespace CentralizedIndexBackupTester
{
    ///<summary>
    /// Prove that it is not necessary to stop indexing during the backup.
    /// </summary>
    /// <remarks>
    /// 1. Ensure the consistent database vs index state.
    /// 2. Start an async editor agent that does index and database operations with the repository API.
    ///    Follow this algorithm:
    ///     a. Start a counter with 0.
    ///     b. Create one content (e.g. SystemFolder) and memorize its Id as "lastId".
    ///     c. If the counter greater than 1, delete the content by term "Id:{lastId-1}".
    ///     d. Increment the counter, wait a second and go to "b".
    /// 3. Wait for 5 sec and do the Backup.
    /// 4. Wait for 5 sec after the Backup is finished.
    /// 5. Finally, check the index integrity with the index and the database.
    ///    The integrity checker need to display no difference.
    /// </remarks>
    public class ContinuousIndexTest : BackupTest
    {
        public ContinuousIndexTest(ILuceneIndexingEngine engine) : base(engine) { }

        protected override IWorker CreateWorker()
        {
            return new ContinuousIndexWorker();
        }
    }
}
