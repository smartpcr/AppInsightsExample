using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus.Client;
using Prometheus.Client.AspNetCore;
using Prometheus.Client.MetricServer;

namespace Common.Instrumentation
{
    public static class PrometheusBuilder
    {
        /// <summary>
        /// this is used in web host
        /// </summary>
        /// <param name="app"></param>
        /// <param name="settings"></param>
        public static void UsePrometheus(this IApplicationBuilder app,
            PrometheusMetricSettings settings)
        {
            app.UsePrometheusServer(options =>
            {
                options.UseDefaultCollectors = true;
                options.MapPath = settings.Route;
            });
        }

        /// <summary>
        /// this is used in console (GenericHost) app
        /// </summary>
        /// <param name="services"></param>
        /// <param name="settings"></param>
        public static void UsePrometheus(this IServiceCollection services, PrometheusMetricSettings settings)
        {
            var metricServer = new MetricServer(null, new MetricServerOptions()
            {
                Port = settings.Port,
                MapPath = settings.Route,
                Host = "localhost",
                UseHttps = settings.UseHttps
            });
            metricServer.Start();
        }
    }
}