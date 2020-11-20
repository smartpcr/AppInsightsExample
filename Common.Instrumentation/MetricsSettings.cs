namespace Common.Instrumentation
{
    public class MetricsSettings
    {
        public bool UseAppInsights { get; set; }
        public bool UsePrometheus { get; set; }
        public PrometheusMetricSettings Prometheus { get; set; }
        public AppInsightsSettings AppInsights { get; set; }
    }

    public class PrometheusMetricSettings
    {
        public string Role { get; set; }
        public string Namespace { get; set; }
        public string Route { get; set; } = "/metrics";
        /// <summary>
        /// only used in console (generic host) app. When app used in k8s, make sure to config containerPort
        /// better option would be always use webhost
        /// </summary>
        public int Port { get; set; }
        public bool UseHttps { get; set; }
    }

    public class AppInsightsSettings
    {
        public string Role { get; set; }
        public string Namespace { get; set; }
        public string Version { get; set; }
        public string[] Tags { get; set; }
    }
}
