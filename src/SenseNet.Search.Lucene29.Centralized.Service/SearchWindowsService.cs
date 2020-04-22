using System;
using System.ServiceModel;
using System.ServiceProcess;
using SenseNet.Search.Lucene29.Centralized.Index;

namespace SenseNet.Search.Lucene29.Centralized.Service
{
    //UNDONE:- Remove from app.config: /configuration/connectionStrings/add[name='SecurityStorage']
    //UNDONE:- Remove from app.config: /configuration/sensenet/rabbitmq/add['ServiceUrl']
    public class SearchWindowsService : ServiceBase
    {
        public ServiceHost ServiceHost;

        public SearchWindowsService()
        {
            ServiceName = "SenseNet.Search.Lucene29.Centralized";
        }

        static void Main(string[] args)
        {
            var service = new SearchWindowsService();

            if (Environment.UserInteractive)
            {
                Console.WriteLine("Starting service.");
                service.OnStart(args);
                Console.WriteLine("Service started.");
                Console.WriteLine("Press any key to stop.");
                Console.ReadKey();
                service.OnStop();
                Console.WriteLine("Service stopped.");
            }
            else
            {
                Run(service);
            }
        }

        protected override void OnStart(string[] args)
        {
            ServiceHost?.Close();
            
            SearchService.Start();

            ServiceHost = new ServiceHost(typeof(SearchService));
            ServiceHost.Open();
        }

        protected override void OnStop()
        {
            SearchService.ShutDown();

            if (ServiceHost == null)
                return;

            ServiceHost.Close();
            ServiceHost = null;
        }
    }
}
