using Grpc.Net.Client;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    public static class GrpcClientExtensions
    {
        public static IRepositoryBuilder UseLucene29CentralizedGrpcServiceClient(this IRepositoryBuilder repositoryBuilder,
            string serviceAddress)
        {
            var channel = GrpcChannel.ForAddress(serviceAddress);
            var searchClient = new GrpcService.GrpcSearch.GrpcSearchClient(channel);
            var serviceClient = new GrpcServiceClient(searchClient);

            repositoryBuilder.UseLucene29CentralizedServiceClient(serviceClient);

            return repositoryBuilder;
        }
    }
}
