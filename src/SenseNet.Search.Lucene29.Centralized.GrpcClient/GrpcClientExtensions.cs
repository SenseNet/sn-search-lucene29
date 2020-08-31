using System;
using System.Net.Http;
using Grpc.Net.Client;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Search.Lucene29.Centralized.GrpcClient;
using SenseNet.Tools;

// ReSharper disable once CheckNamespace
namespace SenseNet.Extensions.DependencyInjection
{
    public static class GrpcClientExtensions
    {
        /// <summary>
        /// Set the centralized Lucene engine as the search engine and
        /// sets the <see cref="GrpcServiceClient"/> as the client for
        /// search service communication.
        /// </summary>
        /// <param name="repositoryBuilder">The <see cref="IRepositoryBuilder"/> instance.</param>
        /// <param name="serviceAddress">Url of the gRPC search service.</param>
        /// <param name="configure">Optional configure method.</param>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngineWithGrpc(
            this IRepositoryBuilder repositoryBuilder,
            string serviceAddress,
            Action<GrpcChannelOptions> configure = null)
        {
            return repositoryBuilder
                .UseLucene29CentralizedSearchEngine()
                .UseLucene29CentralizedGrpcServiceClient(serviceAddress, configure);
        }

        /// <summary>
        /// Set the centralized Lucene engine as the search engine and
        /// sets the <see cref="GrpcServiceClient"/> as the client for
        /// search service communication.
        /// </summary>
        /// <param name="repositoryBuilder">The <see cref="IRepositoryBuilder"/> instance.</param>
        /// <param name="options">Grpc client options</param>
        public static IRepositoryBuilder UseLucene29CentralizedSearchEngineWithGrpc(this IRepositoryBuilder repositoryBuilder, GrpcClientOptions options)
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
                .UseLucene29CentralizedSearchEngine()
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
        
        internal static Search.Lucene29.Centralized.GrpcService.IndexingActivityStatus ToGrpcActivityStatus(this IndexingActivityStatus state)
        {
            var request = new SenseNet.Search.Lucene29.Centralized.GrpcService.IndexingActivityStatus
            {
                LastActivityId = state.LastActivityId
            };
            request.Gaps.AddRange(state.Gaps);

            return request;
        }

        private static GrpcServiceClient CreateGrpcServiceClient(string serviceAddress, GrpcChannelOptions options)
        {
            // this channel will be disposed later, by the GrpcClientSnService class
            var channel = GrpcChannel.ForAddress(serviceAddress, options);
            var searchClient = new Search.Lucene29.Centralized.GrpcService.GrpcSearch.GrpcSearchClient(channel);
            var serviceClient = new GrpcServiceClient(searchClient, channel);

            return serviceClient;
        }
    }
}
