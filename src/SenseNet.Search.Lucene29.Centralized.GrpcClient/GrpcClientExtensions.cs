using System;
using Grpc.Net.Client;
using SenseNet.Diagnostics;
using SenseNet.Search.Indexing;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    public static class GrpcClientExtensions
    {
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

            // this channel will be disposed later, by the GrpcClientSnService class
            var channel = GrpcChannel.ForAddress(serviceAddress, options);
            var searchClient = new GrpcService.GrpcSearch.GrpcSearchClient(channel);
            var serviceClient = new GrpcServiceClient(searchClient, channel);

            repositoryBuilder.UseLucene29CentralizedServiceClient(serviceClient);

            SnLog.WriteInformation("GrpcServiceClient set as Lucene29 Centralized Service Client.");

            return repositoryBuilder;
        }

        internal static GrpcService.IndexingActivityStatus ToGrpcActivityStatus(this IndexingActivityStatus state)
        {
            var request = new GrpcService.IndexingActivityStatus
            {
                LastActivityId = state.LastActivityId
            };
            request.Gaps.AddRange(state.Gaps);

            return request;
        }
    }
}
