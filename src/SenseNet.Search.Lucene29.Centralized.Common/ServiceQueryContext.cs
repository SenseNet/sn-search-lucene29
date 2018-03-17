namespace SenseNet.Search.Lucene29.Centralized.Common
{
    public class ServiceQueryContext
    {
        public int UserId { get; set; }
        public int[] DynamicGroups { get; set; }

        // This is a string because the QueryFieldLevel enum is defined originally in the repository.
        // The enum with the same name in the service is only a mirror of that.
        public string FieldLevel { get; set; }
    }
}
