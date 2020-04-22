using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.Diagnostics;
using SenseNet.Search.Lucene29;
using SenseNet.Security.EFCSecurityStore;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace IndexIntegrityChecker
{
    class Program
    {
        static void Main(string[] args)
        {
            var indexDir = Path.GetFullPath($"{Environment.CurrentDirectory}\\..\\..\\..\\..\\..\\" +
                                                      "SenseNet.Search.Lucene29.Centralized.Service\\" +
                                                      "bin\\Debug\\App_Data\\LocalIndex");

            if (!Directory.Exists(indexDir))
            {
                Console.WriteLine("Directory not found: " + indexDir);
                return;
            }


            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddEnvironmentVariables()
                .Build();

            var serviceIndexPath = Directory.GetDirectories(indexDir).OrderBy(x => x).Last();
            var lockFilePath = Path.Combine(serviceIndexPath, "write.lock");
            while(File.Exists(lockFilePath))
            {
                Console.WriteLine("Wait for write.lock is released.");
                Task.Delay(1000).ConfigureAwait(false).GetAwaiter().GetResult();
            }

            CheckIndexIntegrity(indexDir, configuration);
        }

        static void CheckIndexIntegrity(string indexDirectory, IConfiguration config)
        {
            var builder = new RepositoryBuilder()
                .SetConsole(Console.Out)
                .UseLogger(new SnFileSystemEventLogger())
                .UseTracer(new SnFileSystemTracer())
                .UseConfiguration(config)
                .UseDataProvider(new MsSqlDataProvider())
                .UseSecurityDataProvider(
                    new EFCSecurityDataProvider(connectionString: ConnectionStrings.ConnectionString))
                .UseLucene29LocalSearchEngine(indexDirectory) as RepositoryBuilder;

            using (Repository.Start(builder))
            {
                Console.WriteLine("================================");
                Console.WriteLine("Index integrity:");
                // ----------------------------------------
                //var diffs = IndexIntegrityChecker.Check("/Root", true).ToArray();
                var diffs = IndexIntegrityChecker.Check().ToArray();
                if (diffs.Length != 0)
                {
                    Console.WriteLine($"  Check index integrity failed. Diff count: {diffs.Length}");
                    var count = 0;
                    foreach (var diff in diffs)
                    {
                        Console.WriteLine($"  {diff}");
                        if (++count > 20)
                        {
                            Console.WriteLine($"  ...etc");
                            break;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("ok.");
                }
                Console.WriteLine("================================");
            }
        }
    }
}
