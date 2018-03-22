using System.IO;
using SenseNet.Diagnostics;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    internal class TraceTextWriter : StringWriter
    {
        public override void WriteLine(string value)
        {
            SnTrace.Index.Write(value);
        }
    }
}
