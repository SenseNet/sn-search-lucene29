using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Search;
using SenseNet.Diagnostics;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Search;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Querying;
using SenseNet.Security.Configuration;
using SenseNet.Security.EFCSecurityStore;
using SenseNet.Security.Messaging;
using SenseNet.Security.Messaging.RabbitMQ;
using Serilog;
using DataOptions = SenseNet.Configuration.DataOptions;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace CentralizedIndexBackupTester
{
    class Program
    {
        private enum TestType { Backup, Restore, Validity, Cancellation }

        private static string _serviceIndexDirectory;
        private static string _backupIndexDirectory;
        private static TestType _testType;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Missing test type. Expected 'Backup', 'Restore', 'Validity' or 'Cancellation'.");
                return;
            }
            if(!Enum.TryParse<TestType>(args[0], true, out _testType))
            {
                Console.WriteLine("Invalid test type. Expected 'Backup', 'Restore' or 'Validity'.");
                return;
            }

            _serviceIndexDirectory = Path.GetFullPath($"{Environment.CurrentDirectory}\\..\\..\\..\\..\\..\\" +
                                                      //"SenseNet.Search.Lucene29.Centralized.Service\\" +
                                                      "SenseNet.Search.Lucene29.Centralized.GrpcService\\" +
                                                      "bin\\Debug\\netcoreapp3.1\\App_Data\\LocalIndex");
            Console.WriteLine("IndexDirectory of the service: ");
            Console.WriteLine(_serviceIndexDirectory);

            _backupIndexDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexBackup");
            Console.WriteLine("Backup directory: ");
            Console.WriteLine(_backupIndexDirectory);

            // --------------------------------

            using var host = CreateHostBuilder(args).Build();

            Providers.Instance = new Providers(host.Services);

            var configuration = host.Services.GetService<IConfiguration>();

            var builder = new RepositoryBuilder(host.Services)
                .SetConsole(Console.Out)
                .UseLogger(new SnFileSystemEventLogger())
                .UseLucene29CentralizedSearchEngineWithGrpc(
                    configuration["sensenet:search:service:address"],
                    host.Services.GetService<ILogger<Lucene29SearchEngine>>(),
                    options =>
                {
                    // trust the server in a development environment
                    options.HttpClient = new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
                    });
                    options.DisposeHttpClient = true;
                })
                .UseTraceCategories(SnTrace.Categories.Select(x => x.Name).ToArray()) as RepositoryBuilder;

            // --------------------------------

            //var serviceBinding = new NetTcpBinding { Security = { Mode = SecurityMode.None } };
            //var serviceEndpoint = new EndpointAddress(configuration["sensenet:search:service:address"]);
            //WaitForServiceStarted(serviceBinding, serviceEndpoint);

            //var logger = host.Services.GetService<ILogger<Program>>();
            //var sender = new MessageSenderManager();

            using (Repository.Start(builder))
            {
                Console.WriteLine("CHECK SQL SERVER CONNECTIVITY (query top level nodes):");
                var root = Node.LoadNode(Repository.RootPath);
                foreach (var node in NodeQuery.QueryChildren(Repository.RootPath).Nodes)
                    Console.WriteLine(node.Path);
                Console.WriteLine();

                Console.WriteLine("CHECK SEARCH SERVER CONNECTIVITY (query top level nodes):");
                var queryContext = new SnQueryContext(QuerySettings.AdminSettings, User.Current.Id);
                var result = SnQuery.Query("InFolder:/Root", queryContext);
                foreach(var id in result.Hits)
                    Console.WriteLine(NodeHead.Get(id).Path);
                Console.WriteLine();

                SnTrace.EnableAll();

                var engine = (ILuceneIndexingEngine)Providers.Instance.SearchEngine.IndexingEngine;
                switch (_testType)
                {
                    case TestType.Backup:
                        new ContinuousIndexTest(engine).RunAsync(CancellationToken.None)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                        break;
                    case TestType.Restore:
                        new RestoreTest(engine).RunAsync(CancellationToken.None)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                        break;
                    case TestType.Validity:
                        new ValidityTest(engine).RunAsync(CancellationToken.None)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                        break;
                    case TestType.Cancellation:
                        new CancellationTest(engine).RunAsync(CancellationToken.None)
                            .ConfigureAwait(false).GetAwaiter().GetResult();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                // Shut down the service to leave the index.
                Providers.Instance.IndexManager.IndexingEngine.ShutDownAsync(CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
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
                        .AddSenseNetTracer<SnFileSystemTracer>()
                        .AddRabbitMqSecurityMessageProvider() //TODO: Test this registration
                        ;
                });

        private static void RestoreFiles()
        {
            var target = Directory.GetDirectories(_serviceIndexDirectory).OrderBy(x => x).Last();

            // Ensure empty target
            foreach(var file in Directory.GetFiles(target))
                File.Delete(file);

            // Restore
            foreach (var file in Directory.GetFiles(_backupIndexDirectory))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        private static void WaitForServiceStarted(Binding serviceBinding, EndpointAddress serviceEndpoint)
        {
            Console.WriteLine($"Is write.lock deleted? :)");

            while (true)
            {
                try
                {
                    using (var client = new WcfServiceClient(serviceBinding, serviceEndpoint))
                        client.Alive();
                    return;
                }
                catch(Exception e)
                {
                    Console.WriteLine($"Wait for service ({e.Message})");
                }

                Task.Delay(1000).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
    }
}
