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
        /// <summary>
        /// Sets the Centralized Lucene search engine as the current search engine.
        /// </summary>
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
        [Obsolete("Use the UseLucene29CentralizedSearchEngineWithWcf method instead.")]
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngine(this IRepositoryBuilder repositoryBuilder,
            Binding binding, EndpointAddress address)
        {
            return repositoryBuilder.UseLucene29CentralizedSearchEngineWithWcf(binding, address);
        }
        /// <summary>
        /// Set the centralized Lucene engine as the search engine and initialize it
        /// using the provided WCF endpoint binding.
        /// </summary>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngineWithWcf(this IRepositoryBuilder repositoryBuilder,
            Binding binding, EndpointAddress address)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            
            WcfServiceClient.InitializeServiceEndpoint(binding, address);

            return repositoryBuilder.UseLucene29CentralizedSearchEngine();
        }

        /// <summary>
        /// Sets the provided instance as the client for search service communication.
        /// </summary>
        /// <param name="repositoryBuilder">The <see cref="IRepositoryBuilder"/> instance.</param>
        /// <param name="serviceClient">The client instance.</param>
        public static IRepositoryBuilder UseLucene29CentralizedServiceClient(this IRepositoryBuilder repositoryBuilder,
            ISearchServiceClient serviceClient)
        {
            SearchServiceClient.Instance = serviceClient;

            return repositoryBuilder;
        }
    }
}
