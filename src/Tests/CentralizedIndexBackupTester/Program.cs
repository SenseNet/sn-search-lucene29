using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;
using Microsoft.Extensions.Configuration;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.ContentRepository.Storage.Search;
using SenseNet.Diagnostics;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Querying;
using SenseNet.Security.EFCSecurityStore;
using SenseNet.Security.Messaging.RabbitMQ;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace CentralizedIndexBackupTester
{
    class Program
    {
        private static string _serviceIndexDirectory;
        private static string _backupIndexDirectory;
        private static bool _restoreTest;

        static void Main(string[] args)
        {
            _restoreTest = (args.Length > 0 && args.Any(x => x.ToUpper() == "RESTORE"));
_restoreTest = true;

            _serviceIndexDirectory = Path.GetFullPath($"{Environment.CurrentDirectory}\\..\\..\\..\\..\\..\\" +
                                                      "SenseNet.Search.Lucene29.Centralized.Service\\" +
                                                      "bin\\Debug\\App_Data\\LocalIndex");
            Console.WriteLine("IndexDirectory of the service: ");
            Console.WriteLine(_serviceIndexDirectory);

            _backupIndexDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "IndexBackup");
            Console.WriteLine("Backup directory: ");
            Console.WriteLine(_serviceIndexDirectory);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddEnvironmentVariables()
                .Build();

            var serviceBinding = new NetTcpBinding {Security = {Mode = SecurityMode.None}};
            var serviceEndpoint = new EndpointAddress(configuration["sensenet:search:service:address"]);
            WaitForServiceStarted(serviceBinding, serviceEndpoint);

            var builder = new RepositoryBuilder()
                .SetConsole(Console.Out)
                .UseLogger(new SnFileSystemEventLogger())
                .UseTracer(new SnFileSystemTracer())
                .UseConfiguration(configuration)
                .UseDataProvider(new MsSqlDataProvider())
                .UseSecurityDataProvider(
                    new EFCSecurityDataProvider(connectionString: ConnectionStrings.ConnectionString))
                .UseSecurityMessageProvider(new RabbitMQMessageProvider())
                .UseLucene29CentralizedSearchEngine(serviceBinding, serviceEndpoint)
                .StartWorkflowEngine(false)
                .DisableNodeObservers()
                .UseTraceCategories(SnTrace.Categories.Select(x => x.Name).ToArray()) as RepositoryBuilder;

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
                if (!_restoreTest)
                {
                    // BACKUP TEST
                    new ContinuousIndexTest(engine).RunAsync(CancellationToken.None)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else
                {
                    // RESTORE TEST
                    new RestoreTest(engine).RunAsync(CancellationToken.None)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                }

                // Shut down the service to leave the index.
                IndexManager.IndexingEngine.ShutDownAsync(CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }

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
                    using (var client = new SearchServiceClient(serviceBinding, serviceEndpoint))
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
