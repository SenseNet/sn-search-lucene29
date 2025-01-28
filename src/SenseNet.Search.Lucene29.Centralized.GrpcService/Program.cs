using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using SenseNet.Diagnostics;
using Microsoft.Extensions.Logging;
using Serilog;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // [sensenet] set log and trace before anything happens
            SnLog.Instance = new SnFileSystemEventLogger();
            SnTrace.SnTracers.Add(new SnFileSystemTracer());

            CreateHostBuilder(args).Build().Run();
        }

        // Additional configuration is required to successfully run gRPC on macOS.
        // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                        .ConfigureLogging(loggingConfiguration =>
                            loggingConfiguration.ClearProviders())
                        ;
                })
                .UseSerilog((hostingContext, loggerConfiguration) =>
                    loggerConfiguration.ReadFrom
                        .Configuration(hostingContext.Configuration));
    }
}
