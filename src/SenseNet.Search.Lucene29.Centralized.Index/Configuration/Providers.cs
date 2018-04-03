using System;
using System.Configuration;
using SenseNet.Configuration;
using SenseNet.Diagnostics;
using SenseNet.Security;
using SenseNet.Security.EF6SecurityStore;
using SenseNet.Security.Messaging;
using SenseNet.Tools;

namespace SenseNet.Search.Lucene29.Centralized.Index.Configuration
{
    public class Providers : SnConfig
    {
        private const string SectionName = "sensenet/providers";
        public static Providers Instance { get; set; } = new Providers();

        #region SecurityDataProvider

        public static string SecurityDataProviderClassName { get; internal set; } = GetProvider("SecurityDataProvider",
            "SenseNet.Security.EF6SecurityStore.EF6SecurityDataProvider");

        private Lazy<ISecurityDataProvider> _securityDataProvider = new Lazy<ISecurityDataProvider>(() =>
        {
            ISecurityDataProvider securityDataProvider = null;

            try
            {
                // if other than the known implementation, create it automatically
                if (string.Compare(SecurityDataProviderClassName, typeof(EF6SecurityDataProvider).FullName, StringComparison.Ordinal) != 0)
                    securityDataProvider = (ISecurityDataProvider)TypeResolver.CreateInstance(SecurityDataProviderClassName);
            }
            catch (TypeNotFoundException)
            {
                throw new SnConfigurationException($"Security data provider implementation not found: {SecurityDataProviderClassName}");
            }
            catch (InvalidCastException)
            {
                throw new SnConfigurationException($"Invalid security data provider implementation: {SecurityDataProviderClassName}");
            }

            if (securityDataProvider == null)
            {
                // default implementation
                securityDataProvider = new EF6SecurityDataProvider(
                    Security.SecurityDatabaseCommandTimeoutInSeconds,
                    ConnectionStrings.SecurityDatabaseConnectionString);
            }

            SnLog.WriteInformation("SecurityDataProvider created: " + securityDataProvider.GetType().FullName);

            return securityDataProvider;
        });
        public virtual ISecurityDataProvider SecurityDataProvider
        {
            get => _securityDataProvider.Value;
            set { _securityDataProvider = new Lazy<ISecurityDataProvider>(() => value); }
        }

        #endregion

        #region SecurityMessageProvider

        public static string SecurityMessageProviderClassName { get; internal set; } = GetProvider("SecurityMessageProvider",
            typeof(DefaultMessageProvider).FullName);

        private Lazy<IMessageProvider> _securityMessageProvider = new Lazy<IMessageProvider>(() =>
        {
            var msgProvider = CreateProviderInstance<IMessageProvider>(SecurityMessageProviderClassName,
                "SecurityMessageProvider");
            msgProvider.Initialize();

            return msgProvider;
        });
        public virtual IMessageProvider SecurityMessageProvider
        {
            get => _securityMessageProvider.Value;
            set { _securityMessageProvider = new Lazy<IMessageProvider>(() => value); }
        }

        #endregion

        private static string GetProvider(string key, string defaultValue = null)
        {
            return GetString(SectionName, key, defaultValue);
        }
        private static T CreateProviderInstance<T>(string className, string providerName)
        {
            T provider;

            try
            {
                provider = (T)TypeResolver.CreateInstance(className);
            }
            catch (TypeNotFoundException)
            {
                throw new SnConfigurationException($"{providerName} implementation does not exist: {className}");
            }
            catch (InvalidCastException)
            {
                throw new SnConfigurationException($"Invalid {providerName} implementation: {className}");
            }

            SnLog.WriteInformation($"{providerName} created: {className}");

            return provider;
        }
    }
}
