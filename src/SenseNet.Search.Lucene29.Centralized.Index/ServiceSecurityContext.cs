using System.Collections.Generic;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public class ServiceSecurityContext : SecurityContext
    {
        public ServiceSecurityContext(ISecurityUser currentUser, SecuritySystem securitySystem) : base(currentUser, securitySystem) { }

        #region Published protected base methods and properties

        public new ISecurityUser CurrentUser => base.CurrentUser;

        public override List<AceInfo> GetEffectiveEntries(int contentId, IEnumerable<int> relatedIdentities = null, EntryType? entryType = null)
        {
            return base.GetEffectiveEntries(contentId, relatedIdentities);
        }
        public new List<int> GetGroups()
        {
            return base.GetGroups();
        }
        public new List<int> GetGroupsWithOwnership(int entityId)
        {
            return base.GetGroupsWithOwnership(entityId);
        }
        public new bool HasPermission(int contentId, params PermissionTypeBase[] permissions)
        {
            return base.HasPermission(contentId, permissions);
        }

        #endregion
    }
}
