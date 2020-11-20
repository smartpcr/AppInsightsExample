using System.Threading;
using System.Threading.Tasks;
using Common.DocDb;
using Common.Instrumentation;
using Common.Instrumentation.RuntimeTelemetry;
using Common.KeyVault;
using Example.DocDb.Sync;
using Example.Events.Consumer;
using Example.Events.Producer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppInsightsExample
{
    class Program
    {
        static Program()
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

        static async Task Main(string[] args)
        {
            var builder = new HostBuilder()
                .ConfigureAppConfiguration((hostingContext, configBuilder) =>
                {
                    configBuilder.AddJsonFile("appsettings.json", optional: true);
                })
                .ConfigureServices((hostingContext, services) =>
                {
                    services.AddOptions();
                    services.Configure<VaultSettings>(hostingContext.Configuration.GetSection("Vault"));
                    services.Configure<DocDbSettings>(hostingContext.Configuration.GetSection("DocDb"));
                    services.Configure<AppInsightsSettings>(
                        hostingContext.Configuration.GetSection("AppInsights:Context"));
                    services.Configure<PrometheusMetricSettings>(hostingContext.Configuration.GetSection("Prometheus"));
                    services.AddKeyVault(hostingContext.Configuration);

                    // add app insights
                    var appInsightsSettings = new AppInsightsSettings();
                    hostingContext.Configuration.Bind("AppInsights:Context", appInsightsSettings);
                    var instrumentationKey = services.GetSecret(hostingContext.Configuration, hostingContext.Configuration["AppInsights:InstrumentationKeySecret"]);
                    services.AddAppInsights(appInsightsSettings, instrumentationKey);
                    services.AddLogging(instrumentationKey);

                    // add prometheus
                    var promSettings = new PrometheusMetricSettings();
                    hostingContext.Configuration.Bind("Prometheus", promSettings);
                    services.UsePrometheus(promSettings);
                    
                    // heartbeat and runtime telemetry
                    services.AddHostedService<DotNetRuntimeTelemetryWorker>();
                    // services.AddHostedService<PrometheusHeartbeat>();
                    // services.AddHostedService<AppInsightsHeartbeat>();

                    //RunDocDbSyncJob(hostingContext, services);
                    //RunDocDbBulkSyncJob(hostingContext, services);
                    RunDocDbCopyJob(hostingContext, services);
                    //RunEventJob(hostingContext, services);
                });

            await builder.RunConsoleAsync();
        }

        private static void RunDocDbSyncJob(HostBuilderContext hostingContext, IServiceCollection services)
        {
            services.Configure<DocDbSettings>("source", hostingContext.Configuration.GetSection("SrcDocDb"));
            services.Configure<DocDbSettings>("target", hostingContext.Configuration.GetSection("TgtDocDb"));
            services.Configure<DataSyncSettings>(hostingContext.Configuration.GetSection("SyncSettings"));
            services.AddHostedService<DocDbSyncWorker>();
        }

        private static void RunDocDbBulkSyncJob(HostBuilderContext hostingContext, IServiceCollection services)
        {
            services.Configure<DocDbSettings>("source", hostingContext.Configuration.GetSection("SrcDocDb"));
            services.Configure<DocDbSettings>("target", hostingContext.Configuration.GetSection("TgtDocDb"));
            services.Configure<DataSyncSettings>(hostingContext.Configuration.GetSection("SyncSettings"));
            services.AddHostedService<DocDbBulkSyncWorker>();
        }

        private static void RunDocDbCopyJob(HostBuilderContext hostingContext, IServiceCollection services)
        {
            services.Configure<CopyCollectionsSettings>(hostingContext.Configuration.GetSection("CopyCollectionsSettings"));
            services.AddHostedService<CopyCollectionsWorker>();
        }

        private static void RunEventJob(HostBuilderContext hostingContext, IServiceCollection services)
        {
            services.Configure<DocDbSettings>("SourceDb", hostingContext.Configuration.GetSection("SourceDb"));
            services.Configure<DocDbSettings>("ChangeDb", hostingContext.Configuration.GetSection("ChangeDb"));
            services.Configure<DocDbSettings>("LeaseDb", hostingContext.Configuration.GetSection("LeaseDb"));
            services.Configure<ChangeTrackSettings>(hostingContext.Configuration.GetSection("ChangeTracking"));
            services.AddHostedService<ChangeDocumentConsumer>();
        }
    }
}
