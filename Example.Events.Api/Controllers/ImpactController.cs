using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common.Instrumentation;
using Example.Events.Api.Services;
using Example.Events.Producer;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Example.Events.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ImpactController : ControllerBase
    {
        private readonly ILogger<ImpactController> _logger;
        private readonly TelemetryClient _telemetry;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IChangeTracker _changeTracker;

        public ImpactController(
            ILoggerFactory loggerFactory,
            TelemetryClient telemetry,
            IHttpContextAccessor contextAccessor,
            IChangeTracker changeTracker)
        {
            _logger = loggerFactory.CreateLogger<ImpactController>();
            _telemetry = telemetry;
            _contextAccessor = contextAccessor;
            _changeTracker = changeTracker;
        }

        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return new string[] { "value1", "value2" };
        }

        [HttpGet("find/{payloadId}")]
        public async Task<IEnumerable<ChangeProcess>> TrackImpact(string payloadId)
        {
            using (this.StartOperation(_telemetry, _contextAccessor))
            {
                return await _changeTracker.FindChangeProcesses(payloadId);
            }
        }

        [HttpGet("{count}")]
        public async Task<IEnumerable<ChangeDocument>> CreateChanges(int count)
        {
            using (this.StartOperation(_telemetry, _contextAccessor))
            {
                return await _changeTracker.CreateChanges(count);
            }
        }
    }
}
