using Grpc.Net.Client;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    public class GrpcClientOptions
    {
        public string ServiceAddress { get; set; }
        public GrpcChannelOptions ChannelOptions { get; set; } = new GrpcChannelOptions();
    }
}
