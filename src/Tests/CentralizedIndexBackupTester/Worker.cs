using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.Search;
using Task = System.Threading.Tasks.Task;

namespace CentralizedIndexBackupTester
{
    public class Worker
    {
        private readonly TimeSpan _wait1Sec = TimeSpan.FromSeconds(1);

        private static readonly string TestRootName = "IndexBackupTest";
        private static readonly string TestRootPath = "/Root/" + TestRootName;

        public async Task Work(CancellationToken cancellationToken)
        {
            var words = ("Drop-dead date you better eat a reality sandwich before " +
                        "you walk back in that boardroom.").Split(' ');

            var testFile = await GetTestFile(cancellationToken);

            for (var i = 0; ; i++)
            {
                await Task.Delay(_wait1Sec, cancellationToken);
                //if (cancellationToken.IsCancellationRequested)
                //{
                //    Console.WriteLine("Work finished");
                //    return;
                //}
                //Console.WriteLine("Work");

                //var queryResult = ContentQuery.Query($"Name:'{testFile.Name}' .AUTOFILTERS:OFF");
                //var file = queryResult.Nodes.FirstOrDefault() as File;
                //if (file == null)
                //    throw new ApplicationException("File not found: " + testFile.Path);

                //var text = RepositoryTools.GetStreamString(file.Binary.GetStream());
                //text += words[i % words.Length] + " ";

                //file.Binary.SetStream(RepositoryTools.GetStreamFromString(text));
                //file.Save();
            }
        }
        private async Task<File> GetTestFile(CancellationToken cancellationToken)
        {
            var testRoot = await Node.LoadAsync<SystemFolder>(TestRootPath, cancellationToken);
            if (testRoot == null)
            {
                testRoot = new SystemFolder(Repository.Root) { Name = TestRootName };
                testRoot.Save();
            }

            var testFile = new File(testRoot) { Name = Guid.NewGuid().ToString() };
            testFile.Binary.SetStream(RepositoryTools.GetStreamFromString("Start. "));
            testFile.Save();

            return testFile;
        }
    }
}
