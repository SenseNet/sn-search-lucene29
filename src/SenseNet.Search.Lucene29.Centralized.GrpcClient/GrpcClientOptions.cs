﻿using Grpc.Net.Client;
using SenseNet.Tools.Configuration;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    /// <summary>
    /// Options for the gRPC search service client.
    /// </summary>
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
        /// <summary>
        /// An options class for configuring a GrpcChannel. For more information, see
        /// the Grpc.Net.Client.GrpcChannelOptions documentation.
        /// </summary>
        public GrpcChannelOptions ChannelOptions { get; set; } = new GrpcChannelOptions();
    }
}
