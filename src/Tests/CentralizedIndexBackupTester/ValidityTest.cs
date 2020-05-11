using System;
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
    /// <summary>
    /// 1. Ensure the consistent database vs index state.
    /// 2. Start an async editor agent that does index operations without any database modification.
    ///    Follow this algorithm:
    ///    a. Start a counter with 0.
    ///    b. Add one document: "Id" field with the counter and "Time" field that contains the current datetime
    ///       in "yyyy-MM-dd HH:mm:ss.ffff" format.
    ///    c. If the counter greater than 1, delete the document by term "Id:{counter-1}".
    ///    d. Increment the counter, wait for a second and go to "b".
    /// 3. Wait for 5 sec and do the Backup.
    /// 4. Finally, check the index integrity with the index from the backup and the original database.
    ///    The integrity checker need to display 1 or 2 difference:
    ///    a. In one of them, the "Time" field is closer to the backup time than a second.
    ///    b. The Id of another document (if exists) needs to be one smaller.
    /// </summary>
    public class ValidityTest : BackupTest
    {
        public ValidityTest(ILuceneIndexingEngine engine) : base(engine) { }

        protected override IWorker CreateWorker()
        {
            return new ValidityWorker(_engine);
        }
    }
}
