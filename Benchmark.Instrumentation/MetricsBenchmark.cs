using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Benchmark.Instrumentation
{
    public class MetricsBenchmark : IHostedService
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<MetricsBenchmark> _logger;
        private readonly Metric _heartbeat;
        private int _count;

        public MetricsBenchmark(
            TelemetryClient telemetryClient,
            ILogger<MetricsBenchmark> logger)
        {
            _telemetryClient = telemetryClient;
            _logger = logger;
            _heartbeat = _telemetryClient.GetMetric("heartbeat");
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _count = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                _count++;
                _logger.LogInformation($"thump thump: {_count}");
                _heartbeat.TrackValue(1);
                Thread.Sleep(100);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _telemetryClient.Flush();
            return Task.CompletedTask;
        }
    }
}