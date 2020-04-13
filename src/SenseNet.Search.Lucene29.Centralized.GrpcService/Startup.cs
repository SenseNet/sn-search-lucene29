using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SenseNet.Configuration;
using SenseNet.Search.Lucene29.Centralized.Index.Configuration;
using SenseNet.Security.EFCSecurityStore;
using SenseNet.Security.Messaging.RabbitMQ;

namespace SenseNet.Search.Lucene29.Centralized.GrpcService
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();

            // [sensenet] Search service singleton. This instance will be used
            // by the communication layer to route incoming client calls to the
            // index layer.
            services.AddSingleton<Index.SearchService>();
            
            // [sensenet] Security db and message providers.
            Providers.Instance.SecurityDataProvider = new EFCSecurityDataProvider(
                    Index.Configuration.Security.SecurityDatabaseCommandTimeoutInSeconds,
                    ConnectionStrings.SecurityDatabaseConnectionString);
            Providers.Instance.SecurityMessageProvider = new RabbitMQMessageProvider();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime)
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
            appLifetime.ApplicationStarted.Register(() => Index.SearchService.Start());
            appLifetime.ApplicationStopping.Register(() => Index.SearchService.ShutDown());
        }
    }
}
