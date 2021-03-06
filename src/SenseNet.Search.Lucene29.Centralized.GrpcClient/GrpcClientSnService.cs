﻿using SenseNet.ContentRepository.Storage;
using SenseNet.Diagnostics;

namespace SenseNet.Search.Lucene29.Centralized.GrpcClient
{
    /// <summary>
    /// Technical service for shutting down the search client properly.
    /// This class is instantiated only by the sensenet repository process.
    /// </summary>
    internal class GrpcClientSnService : ISnService
    {
        public void Shutdown()
        {
            var client = SearchServiceClient.Instance as GrpcServiceClient;

            SnTrace.System.Write("Shutting down GrpcServiceClient.");

            client?.ShutDown();
        }

        public bool Start()
        {
            // do nothing
            return true;
        }
    }
}
