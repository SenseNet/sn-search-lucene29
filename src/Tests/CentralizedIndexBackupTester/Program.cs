using System;
using System.ServiceModel;
using System.Threading;
using Microsoft.Extensions.Configuration;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.ContentRepository.Storage.Search;
using SenseNet.Diagnostics;
using SenseNet.Search;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Querying;
using SenseNet.Security.EFCSecurityStore;
using SenseNet.Security.Messaging.RabbitMQ;

namespace CentralizedIndexBackupTester
{
    class Program
    {
        static void Main(string[] args)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddEnvironmentVariables()
                .Build();

            var builder = new RepositoryBuilder()
                .SetConsole(Console.Out)
                .UseLogger(new SnFileSystemEventLogger())
                .UseTracer(new SnFileSystemTracer())
                .UseConfiguration(configuration)
                .UseDataProvider(new MsSqlDataProvider())
                .UseSecurityDataProvider(
                    new EFCSecurityDataProvider(connectionString: ConnectionStrings.ConnectionString))
                .UseSecurityMessageProvider(new RabbitMQMessageProvider())
                .UseLucene29CentralizedSearchEngine(
                    new NetTcpBinding {Security = {Mode = SecurityMode.None}},
                    new EndpointAddress(configuration["sensenet:search:service:address"]))
                .StartWorkflowEngine(false)
                .DisableNodeObservers()
                .UseTraceCategories("Event", "Custom", "System") as RepositoryBuilder;
                //.UseLucene29LocalSearchEngine($"{Environment.CurrentDirectory}\\App_Data\\LocalIndex") as RepositoryBuilder;

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

                Console.WriteLine("CHECK INDEX INTEGRITY BEFORE");
                Console.Write("Index integrity: ... ");
                //AssertIndexIntegrity();
                Console.WriteLine("skipped.");

                var engine = (ILuceneIndexingEngine)Providers.Instance.SearchEngine.IndexingEngine;
                new BackupTest(engine).Run(CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

                Console.WriteLine("CHECK INDEX INTEGRITY AFTER");
                Console.Write("Index integrity: ... ");
                //AssertIndexIntegrity();
                Console.WriteLine("skipped.");
            }
        }
    }
}
