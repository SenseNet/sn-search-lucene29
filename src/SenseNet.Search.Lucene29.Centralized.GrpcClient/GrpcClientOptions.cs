using Grpc.Net.Client;
using SenseNet.Tools.Configuration;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    [OptionsClass(sectionName: "sensenet:search:service")]
    public class GrpcClientOptions
    {
        /// <summary>
        /// Url for the Grpc channel.
        /// </summary>
        public string ServiceAddress { get; set; }

        /// <summary>
        /// Gets or sets a value that may be used by the app start process to set a certificate validation
        /// callback that skips validation.
        /// Default value is true. Do not set this to false unless in a development environment.
        /// </summary>
        public bool ValidateServerCertificate { get; set; } = true;
        public GrpcChannelOptions ChannelOptions { get; set; } = new GrpcChannelOptions();
    }
}
