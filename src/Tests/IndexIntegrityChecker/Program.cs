using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.Diagnostics;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Security.EFCSecurityStore;
using SenseNet.Security.Messaging;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace IndexIntegrityChecker
{
    class Program
    {
        private static void Main(/*string[] args*/)
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
                // ReSharper disable once StringLiteralTypo
                .AddJsonFile("appsettings.json", true, true)
                .AddEnvironmentVariables()
                .Build();

            var subDirs = Directory.GetDirectories(indexDir);
            var serviceIndexPath = subDirs.Any()
                ? subDirs.OrderBy(x => x).Last()
                : indexDir;

            var lockFilePath = Path.Combine(serviceIndexPath, "write.lock");
            while (File.Exists(lockFilePath))
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
                .UseDataProvider(new MsSqlDataProvider(Options.Create(ConnectionStringOptions.GetLegacyConnectionStrings())))
                .UseSecurityDataProvider(new EFCSecurityDataProvider(
                    new MessageSenderManager(),
                    Options.Create(new SenseNet.Security.EFCSecurityStore.Configuration.DataOptions()
                    {
                        ConnectionString = ConnectionStrings.ConnectionString
                    }),
                    NullLogger<EFCSecurityDataProvider>.Instance))
                .UseLucene29LocalSearchEngine(indexDirectory)
                .UseTraceCategories(SnTrace.Categories.Select(x => x.Name).ToArray()) as RepositoryBuilder;

            using (Repository.Start(builder))
            {
                SnTrace.EnableAll();

                Console.WriteLine("================================");

                var savedIndexDir = Path.Combine(Environment.CurrentDirectory, "App_Data", "SavedIndex");
                if (!Directory.Exists(savedIndexDir))
                    Directory.CreateDirectory(savedIndexDir);

                Console.Write("Saving index: ");
                SaveIndex(savedIndexDir);
                Console.WriteLine("ok.");

                Console.Write("Index integrity: ");
                var diffs = IndexIntegrityChecker.Check().ToArray();

                var diffPath = Path.Combine(savedIndexDir, "indexIntegrity.txt");
                using (var writer = new StreamWriter(diffPath, false))
                {
                    if (diffs.Length != 0)
                    {
                        foreach (var diff in diffs)
                            writer.WriteLine($"  {diff}");
                    }
                    else
                    {
                        writer.WriteLine($"There is no any differences.");
                    }
                }

                if (diffs.Length != 0)
                {
                    Console.WriteLine($"check index integrity failed. Diff count: {diffs.Length}");
                    var count = 0;
                    foreach (var diff in diffs)
                    {
                        Console.WriteLine($"  {diff}");
                        if (++count > 20)
                        {
                            Console.WriteLine("  ...etc");
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

        private static void SaveIndex(string savedIndexDir)
        {
            var checker = new IndexIntegrityChecker();
            checker.SaveCommitUserData(savedIndexDir);
            checker.SaveRawIndex(savedIndexDir);
            checker.SaveIndexDocs(savedIndexDir);
        }
    }
}
