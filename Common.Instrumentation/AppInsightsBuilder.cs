using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Common.Instrumentation
{
  

    /// <summary>
    /// 
    /// </summary>
    public static class AppInsightsBuilder
    {
        
        public static IServiceCollection AddAppInsights(this IServiceCollection services, AppInsightsSettings settings, string instrumentationKey)
        {
            var serviceProvider = services.BuildServiceProvider();
            
            if (serviceProvider.GetService<IHostingEnvironment>() == null)
            {
                // force to use aspnetcore hosting environment so that all modules can be loaded
                services.TryAddSingleton<IHostingEnvironment, HostingEnvironment>();
            }
            
            var appInsightsConfig = TelemetryConfiguration.Active;
            appInsightsConfig.InstrumentationKey = instrumentationKey;
            appInsightsConfig.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
            appInsightsConfig.TelemetryInitializers.Add(new HttpDependenciesParsingTelemetryInitializer());
            appInsightsConfig.TelemetryInitializers.Add(new ContextTelemetryInitializer(settings));

            var module = new DependencyTrackingTelemetryModule();
            module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.ServiceBus");
            module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.EventHubs");
            module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.KeyVault");
            module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.DocumentDB");
            module.Initialize(appInsightsConfig);

            var appFolder = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
            services.TryAddSingleton<ITelemetryChannel>(new ServerTelemetryChannel()
            {
                StorageFolder = appFolder,
                DeveloperMode = true 
            });

            // var telemetryClient = new TelemetryClient();
            // services.AddSingleton(telemetryClient);
            services.AddApplicationInsightsTelemetry(o =>
            {
                o.RequestCollectionOptions.EnableW3CDistributedTracing = true;
                o.InstrumentationKey = instrumentationKey;
                o.EnableDebugLogger = true;
                o.AddAutoCollectedMetricExtractor = true;
                o.EnableAdaptiveSampling = false;
            });

            return services;
        }

        public static IServiceCollection AddLogging(this IServiceCollection services, string instrumentationKey)
        {
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddConsole();
                loggingBuilder.AddApplicationInsights(instrumentationKey);
            });

            return services;
        }

    }

    internal class ContextTelemetryInitializer : ITelemetryInitializer
    {
        private AppInsightsSettings _settings;

        public ContextTelemetryInitializer(AppInsightsSettings serviceContext)
        {
            _settings = serviceContext;
        }

        public void Initialize(ITelemetry telemetry)
        {
            telemetry.Context.Cloud.RoleName = _settings.Role;
            telemetry.Context.Component.Version = _settings.Version;
            if (_settings.Tags?.Any() == true)
            {
                telemetry.Context.GlobalProperties["tags"] = string.Join(",", _settings.Tags);
            }
        }
    }
}