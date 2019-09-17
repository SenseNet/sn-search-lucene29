using SenseNet.Configuration;

namespace SenseNet.Search.Lucene29.Centralized.Index.Configuration
{
    public class Security : SnConfig
    {
        private const string SectionName = "sensenet/security";

        public static int SecurityActivityTimeoutInSeconds { get; internal set; } = GetInt(SectionName, "SecurityActivityTimeoutInSeconds", 120);
        public static int SecurityActivityLifetimeInMinutes { get; internal set; } = GetInt(SectionName, "SecurityActivityLifetimeInMinutes", 25 * 60);
        public static int SecurityDatabaseCommandTimeoutInSeconds { get; internal set; } = GetInt(SectionName, "SecurityDatabaseCommandTimeoutInSeconds", Data.DbCommandTimeout);
        public static int SecurityMonitorRunningPeriodInSeconds { get; internal set; } = GetInt(SectionName, "SecurityMonitorPeriodInSeconds", 30);
    }
}
