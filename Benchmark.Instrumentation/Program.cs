using System.Threading.Tasks;
using Common.Instrumentation;
using Common.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Benchmark.Instrumentation
{
    class Program
    {
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
                    services.AddKeyVault(hostingContext.Configuration);

                    var appInsightsSettings = new AppInsightsSettings();
                    hostingContext.Configuration.Bind("AppInsights:Context", appInsightsSettings);
                    var instrumentationKey = services.GetSecret(hostingContext.Configuration, hostingContext.Configuration["AppInsights:InstrumentationKeySecret"]);
                    services.AddAppInsights(appInsightsSettings, instrumentationKey);
                    services.AddLogging(instrumentationKey);

                    services.AddHostedService<MetricsBenchmark>();
                });

            await builder.RunConsoleAsync();
        }
    }
}
