using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.Diagnostics;
using SenseNet.Extensions.DependencyInjection;
using Serilog;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace IndexIntegrityChecker
{
    class Program
    {
        static void Main(string[] args)
        {
            // args = new[] { @"D:\dev\github\sensenet\src\WebApps\SnWebApplication.Api.Sql.Admin\App_Data\LocalIndex" };

            if (args.Length == 0)
            {
                Console.WriteLine("Missing local index path.");
                return;
            }

            var indexDir = args[0];
            if (!Directory.Exists(indexDir))
            {
                Console.WriteLine("Directory not found: " + indexDir);
                return;
            }

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

            // --------------------------------

            using var host = CreateHostBuilder(args).Build();
            var logger = host.Services.GetService<ILogger<Program>>();

            Providers.Instance = new Providers(host.Services);

            var builder = new RepositoryBuilder(host.Services)
                .SetConsole(Console.Out)
                .UseLogger(new SnFileSystemEventLogger())
                //.UseTracer(new SnFileSystemTracer())
                .UseLucene29LocalSearchEngine(serviceIndexPath)
                .UseTraceCategories(SnTrace.Categories.Select(x => x.Name).ToArray()) as RepositoryBuilder;

            // --------------------------------

            using (Repository.Start(builder))
                CheckIndexIntegrity();
        }
        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder => builder
                    .AddJsonFile("appsettings.json", true, true)
                    .AddUserSecrets<Program>()
                )
                .ConfigureServices((hb, services) =>
                {
                    // [sensenet]: Set options for EFCSecurityDataProvider
                    services.AddOptions<SenseNet.Security.EFCSecurityStore.Configuration.DataOptions>()
                        .Configure<IOptions<ConnectionStringOptions>>((securityOptions, systemConnections) =>
                            securityOptions.ConnectionString = systemConnections.Value.Security);

                    // [sensenet]: add sensenet services
                    services
                        .SetSenseNetConfiguration(hb.Configuration)
                        .AddLogging(logging =>
                        {
                            logging.AddSerilog(new LoggerConfiguration()
                                .ReadFrom.Configuration(hb.Configuration)
                                .CreateLogger());
                        })
                        .ConfigureConnectionStrings(hb.Configuration)
                        .AddPlatformIndependentServices()
                        .AddSenseNetTaskManager()
                        .AddSenseNetMsSqlProviders()
                        .AddSenseNetSecurity()
                        .AddEFCSecurityDataProvider()
                        .AddSingleton<ISnTracer, SnFileSystemTracer>();
                });

        static void CheckIndexIntegrity()
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

        private static void SaveIndex(string savedIndexDir)
        {
            var checker = new IndexIntegrityChecker();
            checker.SaveCommitUserData(savedIndexDir);
            checker.SaveRawIndex(savedIndexDir);
            checker.SaveIndexDocs(savedIndexDir);
        }
    }
}
