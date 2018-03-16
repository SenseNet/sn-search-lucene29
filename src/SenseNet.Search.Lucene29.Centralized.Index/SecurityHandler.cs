using System;
using System.Collections.Generic;
using SenseNet.Diagnostics;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public class SecurityHandler
    {
        public static void StartSecurity()
        {
            var securityDataProvider = Providers.Instance.SecurityDataProvider;
            var messageProvider = Providers.Instance.SecurityMessageProvider;
            var startingThesystem = DateTime.UtcNow;

            //UNDONE: configure security values
            ServiceSecurityContext.StartTheSystem(new SecurityConfiguration
            {
                SecurityDataProvider = securityDataProvider,
                MessageProvider = messageProvider,
                SystemUserId = -1, //Identifiers.SystemUserId,
                VisitorUserId = 6, //Identifiers.VisitorUserId,
                EveryoneGroupId = 8, //Identifiers.EveryoneGroupId,
                OwnerGroupId = 9, //Identifiers.OwnersGroupId,
                SecuritActivityTimeoutInSeconds = 120, //Configuration.Security.SecuritActivityTimeoutInSeconds,
                SecuritActivityLifetimeInMinutes = 25 * 60, //Configuration.Security.SecuritActivityLifetimeInMinutes,
                CommunicationMonitorRunningPeriodInSeconds = 30 //Configuration.Security.SecurityMonitorRunningPeriodInSeconds
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
    }
}
