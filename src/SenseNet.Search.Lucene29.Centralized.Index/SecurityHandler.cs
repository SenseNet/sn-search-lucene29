using System;
using System.Collections.Generic;
using SenseNet.Diagnostics;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    /// <summary>
    /// Central security API entry point. When instatiated, it contains a security context,
    /// therefore it is related to a particular user.
    /// </summary>
    public class SecurityHandler
    {
        //============================================================================================= Static API

        public static void StartSecurity()
        {
            var securityDataProvider = Providers.Instance.SecurityDataProvider;
            var messageProvider = Providers.Instance.SecurityMessageProvider;
            var startingThesystem = DateTime.UtcNow;

            ServiceSecurityContext.StartTheSystem(new SecurityConfiguration
            {
                SecurityDataProvider = securityDataProvider,
                MessageProvider = messageProvider,
                SystemUserId = Identifiers.SystemUserId,
                VisitorUserId = Identifiers.VisitorUserId,
                EveryoneGroupId = Identifiers.EveryoneGroupId,
                OwnerGroupId = Identifiers.OwnersGroupId,
                SecuritActivityTimeoutInSeconds = Configuration.Security.SecurityActivityTimeoutInSeconds,
                SecuritActivityLifetimeInMinutes = Configuration.Security.SecurityActivityLifetimeInMinutes,
                CommunicationMonitorRunningPeriodInSeconds = Configuration.Security.SecurityMonitorRunningPeriodInSeconds
            });

            messageProvider.Start(startingThesystem);

            SnLog.WriteInformation("Security subsystem started in Search service", EventId.RepositoryLifecycle,
                properties: new Dictionary<string, object> {
                    { "DataProvider", securityDataProvider.GetType().FullName },
                    { "MessageProvider", messageProvider.GetType().FullName }
                });
        }

        public static ServiceSecurityContext GetSecurityContext(ISecurityUser user)
        {
            return new ServiceSecurityContext(user);
        }

        //============================================================================================= Instance API

        internal ServiceSecurityContext Context { get; }

        public SecurityHandler(ServiceSecurityContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public bool HasPermission(int nodeId, params PermissionTypeBase[] permissionTypes)
        {
            if (permissionTypes == null)
                throw new ArgumentNullException(nameof(permissionTypes));
            if (permissionTypes.Length == 0)
                return false;
            if (Context.CurrentUser.Id == -1)
                return true;

            return Context.HasPermission(nodeId, permissionTypes);
        }

        public List<AceInfo> GetEffectiveEntries(int contentId, IEnumerable<int> relatedIdentities = null)
        {
            return Context.GetEffectiveEntries(contentId, relatedIdentities);
        }

        public List<int> GetIdentitiesByMembership(int contentId = 0, int ownerId = 0)
        {
            var actualUser = Context.CurrentUser;
            if (actualUser.Id == Identifiers.SystemUserId)
                return new List<int> { Identifiers.SystemUserId };

            List<int> identities;
            if (contentId == 0)
            {
                identities = Context.GetGroups();
            }
            else if (ownerId == 0)
            {
                identities = Context.GetGroupsWithOwnership(contentId);
            }
            else
            {
                identities = Context.GetGroups();
                if (actualUser.Id == ownerId)
                    identities.Add(Identifiers.OwnersGroupId);
            }
            identities.Add(actualUser.Id);

            return identities;
        }
    }
}
