using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus.Client;

namespace Common.Instrumentation
{
    public class AppInsightsHeartbeat : BackgroundService
    {
        private readonly ILogger<AppInsightsHeartbeat> _logger;
        private readonly Metric _heartbeat;
        private int _count;

        public AppInsightsHeartbeat(
            TelemetryClient telemetryClient,
            ILogger<AppInsightsHeartbeat> logger,
            IOptions<AppInsightsSettings> settings)
        {
            _logger = logger;
            var metricId = new MetricIdentifier(settings.Value.Namespace, $"{settings.Value.Role}.heartbeat");
            _heartbeat = telemetryClient.GetMetric(metricId);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _count = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                _count++;
                _logger.LogInformation($"thump thump: {_count}");
                _heartbeat.TrackValue(1);
                Thread.Sleep(100);
            }
            return Task.CompletedTask;
        }
    }

    public class PrometheusHeartbeat : BackgroundService
    {
        private readonly ILogger<PrometheusHeartbeat> _logger;
        private readonly Counter _counter;

        public PrometheusHeartbeat(
            ILogger<PrometheusHeartbeat> logger,
            IOptions<PrometheusMetricSettings> settings)
        {
            _logger = logger;
            _counter = Metrics.CreateCounter(
                $"{settings.Value.Namespace.Replace(".", "_")}" + 
                $"_{settings.Value.Role.Replace(".", "_")}_heartbeat", 
                "help text", "label");
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Prometheus heartbeat started...");
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int count = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                count++;
                _counter.Inc(1);
                _logger.LogInformation($"dhakdhak: {count}");
                await Task.Delay(100, stoppingToken);
            }
        }
    }
}
