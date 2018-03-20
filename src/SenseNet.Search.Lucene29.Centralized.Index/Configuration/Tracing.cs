using System;
using SenseNet.Configuration;

namespace SenseNet.Search.Lucene29.Centralized.Index.Configuration
{
    public class Tracing : SnConfig
    {
        private const string SectionName = "sensenet/tracing";

        public static string[] TraceCategories { get; } = GetString(SectionName, "TraceCategories", string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
