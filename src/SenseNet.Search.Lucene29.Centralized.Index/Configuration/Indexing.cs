using System.IO;
using SenseNet.Diagnostics;

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
                        .Replace('/', Path.DirectorySeparatorChar);
                    var directoryPath = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
                    SnTrace.Index.Write("Indexing directory path: {0}", directoryPath);
                    SnTrace.Index.Write("Indexing config value: {0}", configValue);
                    var cptPlanet = Path.Combine(directoryPath, configValue);
                    SnTrace.Index.Write("Indexing Captain Planet: {0}", cptPlanet);
                    var location = Path.GetFullPath(cptPlanet);
                    SnTrace.Index.Write("Indexing location: {0}", location);
                    //_indexDirectoryPath = location;
                    _indexDirectoryPath = Path.GetFullPath(configValue);
                }
                return _indexDirectoryPath;
            }
        }
    }
}
