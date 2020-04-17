using System;
using Grpc.Net.Client;
using SenseNet.Diagnostics;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    public static class GrpcClientExtensions
    {
        public static IRepositoryBuilder UseLucene29CentralizedGrpcServiceClient(this IRepositoryBuilder repositoryBuilder,
            string serviceAddress,
            Action<GrpcChannelOptions> configure = null)
        {
            var options = new GrpcChannelOptions();
            configure?.Invoke(options);

            var channel = GrpcChannel.ForAddress(serviceAddress, options);
            var searchClient = new GrpcService.GrpcSearch.GrpcSearchClient(channel);
            var serviceClient = new GrpcServiceClient(searchClient, channel);

            repositoryBuilder.UseLucene29CentralizedServiceClient(serviceClient);

            SnLog.WriteInformation("GrpcServiceClient set as Lucene29 Centralized Service Client.");

            return repositoryBuilder;
        }
    }
}
