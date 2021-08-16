using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Diagnostics;
using SenseNet.Security;
using SenseNet.Security.Configuration;
using SenseNet.Security.Messaging;
using SenseNet.Security.Messaging.RabbitMQ;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();

            // [sensenet] Ensure configuration.
            _ = new EmptyRepositoryBuilder()
                .UseConfiguration(Configuration);

            services
                .Configure<RabbitMqOptions>(Configuration.GetSection("sensenet:security:rabbitmq"))
                .Configure<MessagingOptions>(Configuration.GetSection("sensenet:security:messaging"));

            SnLog.Instance = new SnFileSystemEventLogger();
            SnTrace.SnTracers.Add(new SnFileSystemTracer());

            // [sensenet] Search service singleton. This instance will be used
            // by the communication layer to route incoming client calls to the
            // index layer.
            services.AddSingleton<Index.SearchService>();
            
            // [sensenet] Security db and message providers.
            services.AddSenseNetSecurity()
                .AddEFCSecurityDataProvider(options =>
                {
                    options.ConnectionString = ConnectionStrings.SecurityDatabaseConnectionString;
                })
                .AddRabbitMqSecurityMessageProvider();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime,
            ISecurityDataProvider securityDataProvider, 
            IMessageProvider messageProvider, 
            IMissingEntityHandler missingEntityHandler,
            IOptions<MessagingOptions> messagingOptions)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<SearchService>();

                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
                });
            });

            // [sensenet] Start and stop the search service in the appropriate points
            // of the application life cycle.
            appLifetime.ApplicationStarted.Register(() =>
            {
                // set the index directory manually based on the current environment
                Index.SearchService.Start(
                    securityDataProvider, messageProvider, missingEntityHandler, messagingOptions.Value,
                    Path.Combine(Environment.CurrentDirectory, "App_Data", "LocalIndex"));
            });
            appLifetime.ApplicationStopping.Register(Index.SearchService.ShutDown);
        }
    }
}
