using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Client;
using Common.Instrumentation;
using Example.Events.Producer;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Example.Events.Api.Services
{
    public class ChangeTracker : HttpClientBase, IChangeTracker
    {
        private readonly ILogger<ChangeTracker> _logger;
        private readonly TelemetryClient _telemetry;

        public ChangeTracker(HttpClient client,
            ILogger<ChangeTracker> logger,
            TelemetryClient telemetry) : base(client)
        {
            _logger = logger;
            _telemetry = telemetry;
        }

        public async Task<IEnumerable<ChangeDocument>> CreateChanges(int count)
        {
            using (this.StartOperation(_telemetry))
            {
                var requestUrl = $"{Client.BaseAddress}api/changedocuments/{count}";
                _logger.LogInformation($"sending request to '{requestUrl}'...");
                var response = await Client.GetAsync(requestUrl);
                if (response.IsSuccessStatusCode)
                {
                    var changeDocuments = await response.Content.ReadAsAsync<IEnumerable<ChangeDocument>>();
                    _logger.LogInformation($"total of {changeDocuments.Count()} are created");
                    return changeDocuments;
                }
                else
                {
                    var errorMessage = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Status: {response.StatusCode}, error: {errorMessage}");
                    return null;
                }
            }
        }

        public async Task<IEnumerable<ChangeProcess>> FindChangeProcesses(string payloadId)
        {
            using (this.StartOperation(_telemetry))
            {
                var requestUrl = $"{Client.BaseAddress}api/changedocuments/find/{payloadId}";
                var response = await Client.GetAsync(requestUrl);
                return await response.Content.ReadAsAsync<IEnumerable<ChangeProcess>>();
            }
        }
    }
}
