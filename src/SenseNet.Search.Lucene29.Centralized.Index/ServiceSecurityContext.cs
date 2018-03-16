using System.Collections.Generic;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public class ServiceSecurityContext : SecurityContext
    {
        public ServiceSecurityContext(ISecurityUser currentUser) : base(currentUser)
        {
        }

        public new static void StartTheSystem(SecurityConfiguration configuration)
        {
            SecurityContext.StartTheSystem(configuration);
        }

        public new List<AceInfo> GetEffectiveEntries(int contentId, IEnumerable<int> relatedIdentities = null)
        {
            return base.GetEffectiveEntries(contentId, relatedIdentities);
        }
    }
}
