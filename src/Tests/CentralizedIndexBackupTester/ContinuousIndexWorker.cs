using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.Diagnostics;
using SenseNet.Search;
using SenseNet.Search.Querying;
using Task = System.Threading.Tasks.Task;

namespace CentralizedIndexBackupTester
{
    public class ContinuousIndexWorker : IWorker
    {
        private static readonly string TestRootName = "IndexBackupTest";
        private static readonly string TestRootPath = "/Root/" + TestRootName;

        /// <summary>
        /// a. Start a counter with 0.
        /// b. Create one content (e.g. SystemFolder) and memorize its Id as "lastId".
        /// c. If the counter greater than 1, delete the content by term "Id:{lastId-1}".
        /// d. Increment the counter, wait a second and go to "b".
        /// </summary>
        public async Task WorkAsync(CancellationToken cancellationToken)
        {
            //while (true)
            //{
            //    // Exit if needed.
            //    if (cancellationToken.IsCancellationRequested)
            //    {
            //        Console.WriteLine("Work finished");
            //        return;
            //    }

            //    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

            //    var queryContext = new SnQueryContext(QuerySettings.AdminSettings, User.Current.Id);
            //    var result = SnQuery.Query("InFolder:/Root", queryContext);
            //    Console.WriteLine("Work: " + string.Join(" ",result.Hits.Select(x=>x.ToString())));
            //}

            var lastId = 0;
            while (true)
            {
                // Exit if needed.
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Work finished");
                    return;
                }
                Console.WriteLine("Work");

                SnTrace.Write("#### wait");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                // Create one content (e.g. SystemFolder)...
                var node = await CreateNode(cancellationToken);
                SnTrace.Write("#### node created: " + node.Id);

                // Delete the last created content.
                if (lastId > 0)
                {
                    Node.ForceDelete(lastId);
                    SnTrace.Write("#### node deleted: " + lastId);
                }

                // ... and memorize its Id as "lastId".
                lastId = node.Id;
            }
        }
        private async Task<Node> CreateNode(CancellationToken cancellationToken)
        {
            var testRoot = await Node.LoadAsync<SystemFolder>(TestRootPath, cancellationToken);
            if (testRoot == null)
            {
                testRoot = new SystemFolder(Repository.Root) { Name = TestRootName };
                testRoot.Save();
            }

            var testFolder = new SystemFolder(testRoot) { Name = Guid.NewGuid().ToString() };
            testFolder.Save();

            return testFolder;
        }
    }
}
