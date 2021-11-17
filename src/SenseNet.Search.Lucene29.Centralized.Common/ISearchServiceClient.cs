namespace SenseNet.Search.Lucene29.Centralized.Common
{
    public interface ISearchServiceClient : ISearchServiceContract
    {
        /// <summary>
        /// Returns a new or the singleton implementation instance.
        /// </summary>
        /// <returns>An <see cref="ISearchServiceClient"/> instance.</returns>
        ISearchServiceClient CreateInstance();

        /// <summary>
        /// Starts the underlying client and opens the connection.
        /// </summary>
        void Start();
        /// <summary>
        /// Stops the underlying client and closes the connection.
        /// </summary>
        void ShutDown();
    }
}
