using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace SenseNet.Search.Lucene29.Centralized.Service
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            var process = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem
            };

            var service = new ServiceInstaller
            {
                ServiceName = "SenseNet.Search.Lucene29.Centralized",
                StartType = ServiceStartMode.Automatic
            };

            Installers.Add(process);
            Installers.Add(service);
        }
    }
}
