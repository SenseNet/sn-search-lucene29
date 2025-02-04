using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Lucene29.Centralized;
using SenseNet.Search.Lucene29.Centralized.Common;
using SenseNet.Tools;

// ReSharper disable once CheckNamespace
namespace SenseNet.Extensions.DependencyInjection
{
    public static class Lucene29CentralizedExtensions
    {
        /// <summary>
        /// Sets the Centralized Lucene search engine as the current search engine.
        /// </summary>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngine(this IRepositoryBuilder repositoryBuilder,
            ILogger<Lucene29SearchEngine> logger)
        {
            var searchEngine = new Lucene29SearchEngine(
                new Lucene29CentralizedIndexingEngine(null, Options.Create(new CentralizedOptions())),
                new Lucene29CentralizedQueryEngine(),
                logger);

            repositoryBuilder.UseSearchEngine(searchEngine);

            return repositoryBuilder;
        }
        /// <summary>
        /// Set the centralized Lucene engine as the search engine and initialize it
        /// using the provided WCF endpoint binding.
        /// </summary>
        [Obsolete("Use the UseLucene29CentralizedSearchEngineWithWcf method instead.")]
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngine(this IRepositoryBuilder repositoryBuilder,
            Binding binding, EndpointAddress address, ILogger<Lucene29SearchEngine> logger)
        {
            return repositoryBuilder.UseLucene29CentralizedSearchEngineWithWcf(binding, address, logger);
        }
        /// <summary>
        /// Set the centralized Lucene engine as the search engine and initialize it
        /// using the provided WCF endpoint binding.
        /// </summary>
        [Obsolete("Do not use this technology anymore.", true)]
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngineWithWcf(this IRepositoryBuilder repositoryBuilder,
            Binding binding, EndpointAddress address, ILogger<Lucene29SearchEngine> logger)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            
            WcfServiceClient.InitializeServiceEndpoint(binding, address);

            return repositoryBuilder.UseLucene29CentralizedSearchEngine(logger);
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

        /// <summary>
        /// Adds the Lucene29 centralized search engine to the service collection.
        /// </summary>
        public static IServiceCollection AddLucene29CentralizedSearchEngine(this IServiceCollection services)
        {
            services
                .Configure<CentralizedOptions>(configure => {})
                .AddSenseNetIndexingEngine<Lucene29CentralizedIndexingEngine>()
                .AddSenseNetQueryEngine<Lucene29CentralizedQueryEngine>()
                .AddSenseNetSearchEngine<Lucene29SearchEngine>();

            return services;
        }
        /// <summary>
        /// Registers the provided instance as the client for search service communication.
        /// </summary>
        public static IServiceCollection AddLucene29CentralizedServiceClient(this IServiceCollection services,
            Func<IServiceProvider, ISearchServiceClient> implementationFactory)
        {
            services.AddSingleton(implementationFactory);

            return services;
        }
        /// <summary>
        /// Registers the provided type as the client for search service communication.
        /// </summary>
        public static IServiceCollection AddLucene29CentralizedServiceClient<T>(this IServiceCollection services) where T: class, ISearchServiceClient
        {
            services.AddSingleton<ISearchServiceClient, T>();

            return services;
        }
    }
}
