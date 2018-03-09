using System.ServiceModel;
using System.ServiceProcess;

namespace SenseNet.Search.Lucene29.Centralized.Service
{
    public class SearchWindowsService : ServiceBase
    {
        public ServiceHost ServiceHost;

        public SearchWindowsService()
        {
            ServiceName = "SenseNet.Search.Lucene29.Centralized";
        }

        public static void Main()
        {
            Run(new SearchWindowsService());
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
