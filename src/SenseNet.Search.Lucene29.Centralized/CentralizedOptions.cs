namespace SenseNet.Search.Lucene29.Centralized
{
    public class CentralizedOptions
    {
        /// <summary>
        /// Number of items sent to the central service in one round. Default is 20.
        /// </summary>
        public int ServiceWritePartitionSize { get; set; } = 20;
    }
}
