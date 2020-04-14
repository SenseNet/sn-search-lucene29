using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using SenseNet.ContentRepository;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29
{
    public static class Lucene29CentralizedExtensions
    {
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngine(this IRepositoryBuilder repositoryBuilder)
        {
            var searchEngine = new Lucene29SearchEngine()
            {
                IndexingEngine = new Lucene29CentralizedIndexingEngine(),
                QueryEngine = new Lucene29CentralizedQueryEngine()
            };

            repositoryBuilder.UseSearchEngine(searchEngine);

            return repositoryBuilder;
        }
        /// <summary>
        /// Set the centralized Lucene engine as the search engine and initialize it
        /// using the provided WCF endpoint binding.
        /// </summary>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngine(this IRepositoryBuilder repositoryBuilder,
            Binding binding, EndpointAddress address)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (address == null)
                throw new ArgumentNullException(nameof(address));

            SearchServiceClient.InitializeServiceEndpoint(binding, address);

            return repositoryBuilder.UseLucene29CentralizedSearchEngine();
        }

        public static IRepositoryBuilder UseLucene29CentralizedServiceClient(this IRepositoryBuilder repositoryBuilder,
            ISearchServiceContract serviceClient)
        {
            SearchServiceClient.Instance = serviceClient;

            return repositoryBuilder;
        }
    }
}
