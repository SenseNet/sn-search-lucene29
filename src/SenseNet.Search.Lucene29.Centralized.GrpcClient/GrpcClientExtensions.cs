using System;
using System.Net.Http;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29;
using SenseNet.Search.Lucene29.Centralized.GrpcClient;
using SenseNet.Tools;

// ReSharper disable once CheckNamespace
namespace SenseNet.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for setting up the Lucene29 
    /// centralized search engine with the gRPC client.
    /// </summary>
    public static class GrpcClientExtensions
    {
        /// <summary>
        /// Set the centralized Lucene engine as the search engine and
        /// sets the <see cref="GrpcServiceClient"/> as the client for
        /// search service communication.
        /// </summary>
        /// <param name="repositoryBuilder">The <see cref="IRepositoryBuilder"/> instance.</param>
        /// <param name="serviceAddress">Url of the gRPC search service.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="configure">Optional configure method.</param>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngineWithGrpc(
            this IRepositoryBuilder repositoryBuilder,
            string serviceAddress,
            ILogger<Lucene29SearchEngine> logger,
            Action<GrpcChannelOptions> configure = null)
        {
            return repositoryBuilder
                .UseLucene29CentralizedSearchEngine(logger)
                .UseLucene29CentralizedGrpcServiceClient(serviceAddress, configure);
        }

        /// <summary>
        /// Set the centralized Lucene engine as the search engine and
        /// sets the <see cref="GrpcServiceClient"/> as the client for
        /// search service communication.
        /// </summary>
        /// <param name="repositoryBuilder">The <see cref="IRepositoryBuilder"/> instance.</param>
        /// <param name="options">Grpc client options</param>
        /// <param name="logger">Logger instance.</param>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngineWithGrpc(this IRepositoryBuilder repositoryBuilder,
            GrpcClientOptions options, ILogger<Lucene29SearchEngine> logger)
        {
            //TODO: refactor the Grpc client to be able to work as a hosted service and use dependency injection correctly

            // shortcut for bypassing certificate validation using a single configurable flag
            if (!options.ValidateServerCertificate)
            {
                options.ChannelOptions.HttpClient = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
                });
                options.ChannelOptions.DisposeHttpClient = true;
            }

            repositoryBuilder
                .UseLucene29CentralizedSearchEngine(logger)
                .UseLucene29CentralizedServiceClient(CreateGrpcServiceClient(options.ServiceAddress, options.ChannelOptions));

            SnLog.WriteInformation("GrpcServiceClient set as Lucene29 Centralized Service Client.");

            return repositoryBuilder;
        }

        /// <summary>
        /// Sets the <see cref="GrpcServiceClient"/> as the client for
        /// search service communication.
        /// </summary>
        /// <param name="repositoryBuilder">The <see cref="IRepositoryBuilder"/> instance.</param>
        /// <param name="serviceAddress">Url of the gRPC search service.</param>
        /// <param name="configure">Optional configure method.</param>
        public static IRepositoryBuilder UseLucene29CentralizedGrpcServiceClient(this IRepositoryBuilder repositoryBuilder,
            string serviceAddress,
            Action<GrpcChannelOptions> configure = null)
        {
            var options = new GrpcChannelOptions();
            configure?.Invoke(options);
            
            repositoryBuilder.UseLucene29CentralizedServiceClient(CreateGrpcServiceClient(serviceAddress, options));

            SnLog.WriteInformation("GrpcServiceClient set as Lucene29 Centralized Service Client.");

            return repositoryBuilder;
        }

        /// <summary>
        /// Registers the centralized search engine and the Grpc client in the service collection.
        /// </summary>
        public static IServiceCollection AddLucene29CentralizedSearchEngineWithGrpc(this IServiceCollection services,
            Action<GrpcClientOptions> configure = null)
        {
            services.Configure<GrpcClientOptions>(options =>
            {
                if (options.ValidateServerCertificate) 
                    return;
                options.ChannelOptions.HttpClient = new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
                });

                // Do not set the DisposeHttpClient property to True because the client may be used
                // multiple times when starting and stopping the service in a single app domain.
            });

            if (configure != null)
                services.Configure(configure);

            services
                .AddLucene29CentralizedSearchEngine()
                .AddLucene29CentralizedServiceClient<GrpcServiceClient>();
            return services;
        }

        internal static Search.Lucene29.Centralized.GrpcService.IndexingActivityStatus ToGrpcActivityStatus(this IndexingActivityStatus state)
        {
            var request = new SenseNet.Search.Lucene29.Centralized.GrpcService.IndexingActivityStatus
            {
                LastActivityId = state.LastActivityId
            };
            request.Gaps.AddRange(state.Gaps);

            return request;
        }

        [Obsolete("Use dependency injection instead.")]
        private static GrpcServiceClient CreateGrpcServiceClient(string serviceAddress, GrpcChannelOptions options)
        {
            // this channel will be disposed later, by the GrpcClientSnService class
            var channel = GrpcChannel.ForAddress(serviceAddress, options);
            var searchClient = new Search.Lucene29.Centralized.GrpcService.GrpcSearch.GrpcSearchClient(channel);
            var serviceClient = new GrpcServiceClient(searchClient, channel, options);

            return serviceClient;
        }
    }
}
