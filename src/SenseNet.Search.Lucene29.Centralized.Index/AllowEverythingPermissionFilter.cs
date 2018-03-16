using SenseNet.Search.Querying;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    /// <summary>
    /// A built-in permission filter that allows everything.
    /// </summary>
    internal class AllowEverythingPermissionFilter : IPermissionFilter
    {
        public bool IsPermitted(int nodeId, bool isLastPublic, bool isLastDraft)
        {
            return true;
        }
    }
}