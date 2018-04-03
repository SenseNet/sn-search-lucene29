namespace SenseNet.Configuration
{
    public class Indexing : SnConfig
    {
        private const string SectionName = "sensenet/indexing";

        private static string _indexDirectoryPath;
        internal static string IndexDirectoryFullPath
        {
            get
            {
                if (_indexDirectoryPath == null)
                {
                    // Note that this is different from the original default index directory as it defines the folder right
                    // INSIDE the execution folder as opposed to the parent folder (like in case of the distributed
                    // solution on web servers).

                    var configValue = GetString(SectionName, "IndexDirectoryPath", Lucene29.DefaultLocalIndexDirectory);
                    var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().CodeBase
                        .Replace("file:///", "")
                        .Replace("file://", "//")
                        .Replace("/", "\\");
                    var directoryPath = System.IO.Path.GetDirectoryName(assemblyPath) ?? string.Empty;

                    _indexDirectoryPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(directoryPath, configValue));
                }
                return _indexDirectoryPath;
            }
        }
    }
}
