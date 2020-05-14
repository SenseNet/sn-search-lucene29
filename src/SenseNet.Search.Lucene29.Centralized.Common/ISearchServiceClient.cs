using System;
using System.Collections.Generic;
using System.Text;

namespace SenseNet.Search.Lucene29.Centralized.Common
{
    public interface ISearchServiceClient : ISearchServiceContract
    {
        /// <summary>
        /// Returns a new or the singleton implementation instance.
        /// </summary>
        /// <returns>An <see cref="ISearchServiceClient"/> instance.</returns>
        ISearchServiceClient CreateInstance();
    }
}
