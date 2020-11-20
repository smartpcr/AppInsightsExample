using System.Collections.Generic;

namespace Common.Instrumentation
{
    /// <summary>
    /// wrapper to metric provider
    /// </summary>
    public interface IMetricsProducer
    {
        MetricProviderType ProviderType { get; }
        void RecordMetric(string name, double value, params KeyValuePair<string, string>[] labels);
    }

    public enum MetricProviderType
    {
        ApplicationInsights,
        Stats,
        Geneva,
        Prometheus
    }
}