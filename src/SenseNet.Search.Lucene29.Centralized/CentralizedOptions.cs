using Grpc.Net.Client;
using SenseNet.Tools.Configuration;

namespace SenseNet.Search.Lucene29.Centralized
{
    /// <summary>
    /// Options for the centralized search engine.
    /// </summary>
    [OptionsClass("sensenet:search:service")]
    public class CentralizedOptions
    {
        /// <summary>
        /// Number of items sent to the central service in one round. Default is 20.
        /// </summary>
        public int ServiceWritePartitionSize { get; set; } = 20;

        /// <summary>
        /// An options class for configuring a GrpcChannel. For more information, see
        /// the Grpc.Net.Client.GrpcChannelOptions documentation.
        /// </summary>
        public GrpcChannelOptions ChannelOptions { get; set; } = new GrpcChannelOptions();
    }
}
