using System.Linq;
using SenseNet.Search.Lucene29;

namespace SenseNet.Packaging.Steps
{
    [Annotation("Checks the index integrity by comparation the index and database.")]
    public class CheckIndexIntegrity : Step
    {
        [DefaultProperty]
        [Annotation("Defines the integrity check's scope if there is. If empty, the whole repository tree will be checked.")]
        public string Path { get; set; }

        [Annotation("Defines whether check only one content or the whole tree or subtree. Default: true.")]
        public bool Recursive { get; set; } = true;

        [Annotation("Limits the output line count. 0 means all lines. Default: 1000.")]
        public int OutputLimit { get; set; } = 1000;

        public override void Execute(ExecutionContext context)
        {
            Logger.LogMessage("CheckIndexIntegrity step is not supported anymore.");

            throw new SnNotSupportedException("CheckIndexIntegrity step is not supported anymore.");
        }
    }
}
