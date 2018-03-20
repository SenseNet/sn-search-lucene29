using SenseNet.Configuration;

namespace SenseNet.Search.Lucene29.Centralized.Index.Configuration
{
    public class Security : SnConfig
    {
        private const string SectionName = "sensenet/security";

        public static int SecuritActivityTimeoutInSeconds { get; internal set; } = GetInt(SectionName, "SecuritActivityTimeoutInSeconds", 120);
        public static int SecuritActivityLifetimeInMinutes { get; internal set; } = GetInt(SectionName, "SecuritActivityLifetimeInMinutes", 25 * 60);
        public static int SecurityDatabaseCommandTimeoutInSeconds { get; internal set; } = GetInt(SectionName, "SecurityDatabaseCommandTimeoutInSeconds", Data.SqlCommandTimeout);
        public static int SecurityMonitorRunningPeriodInSeconds { get; internal set; } = GetInt(SectionName, "SecurityMonitorPeriodInSeconds", 30);
    }
}
