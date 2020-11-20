using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.DocDb;
using Common.Instrumentation;
using Common.KeyVault;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Example.Events.Producer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChangeDocumentsController : ControllerBase, IDisposable
    {
        private readonly ILogger<ChangeDocumentsController> _logger;
        private readonly TelemetryClient _telemetry;
        private readonly IHttpContextAccessor _contextAccessor;

        private DocumentDbClient _sourceClient;
        private DocumentDbClient _targetClient;

        public ChangeDocumentsController(
            IKeyVaultClient kvClient,
            IOptions<VaultSettings> vaultSettings,
            IOptionsSnapshot<DocDbSettings> sourceDb,
            IOptionsSnapshot<DocDbSettings> changeDb,
            ILoggerFactory loggerFactory,
            TelemetryClient telemetry,
            IHttpContextAccessor contextAccessor)
        {
            var sourceDbSettings = sourceDb.Get("SourceDb");
            _sourceClient = new DocumentDbClient(
                kvClient, vaultSettings, sourceDbSettings,
                loggerFactory.CreateLogger<DocumentDbClient>(),
                telemetry);
            var changeDbSetting = changeDb.Get("ChangeDb");
            _targetClient = new DocumentDbClient(
                kvClient, vaultSettings, changeDbSetting,
                loggerFactory.CreateLogger<DocumentDbClient>(),
                telemetry);
            _logger = loggerFactory.CreateLogger<ChangeDocumentsController>();
            _telemetry = telemetry;
            _contextAccessor = contextAccessor;
        }

        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return new string[] { "value1", "value2" };
        }

        [HttpGet("{count}")]
        public async Task<IEnumerable<ChangeDocument>> Get(int count)
        {
            using (this.StartOperation(_telemetry, _contextAccessor))
            {
                var docs = await GetChangeDocuments(count);
                var changeDocuments = await SaveChangeDocuments(docs);
                return changeDocuments;
            }
        }

        public void Dispose()
        {
            if (_sourceClient != null)
            {
                _sourceClient.Dispose();
                _sourceClient = null;
            }
            if (_targetClient != null)
            {
                _targetClient.Dispose();
                _targetClient = null;
            }
        }


        private async Task<IEnumerable<Document>> GetChangeDocuments(int count, CancellationToken cancel = default)
        {
            using (this.StartOperation(_telemetry, _contextAccessor))
            {
                var sourceLink = _sourceClient.Collection.SelfLink;
                _logger.LogInformation($"Getting change documents from {sourceLink}");
                var queryString = $"select top {count} * from c";
                var documents = await _sourceClient.Query<Document>(new SqlQuerySpec(queryString));
                var docs = documents.ToList();

                _logger.LogInformation($"Total of {docs.Count} change documents detected in {sourceLink}.");
                return docs;
            }
        }

        private async Task<IEnumerable<ChangeDocument>> SaveChangeDocuments(IEnumerable<Document> changes, CancellationToken cancel = default)
        {
            using (this.StartOperation(_telemetry))
            {
                var targetLink = _targetClient.Collection.SelfLink;
                _logger.LogInformation($"Saving change documents to {targetLink}");
                var changeDocuments = new List<ChangeDocument>();

                foreach (var change in changes)
                {
                    var changeDocument = await SaveChangeDocument(change);
                    changeDocuments.Add(changeDocument);
                }

                _logger.LogInformation($"Total of {changeDocuments.Count} change documents are saved in {targetLink}.");

                return changeDocuments;
            }
        }

        private async Task<ChangeDocument> SaveChangeDocument(Document doc)
        {
            using (var operation = this.StartOperation(_telemetry))
            {
                Random rand = new Random();
                var change = new ChangeDocument
                {
                    PartitionKey = doc.GetPropertyValue<string>("documentType"),
                    Timestamp = DateTime.UtcNow,
                    ChangeType = (ChangeType)rand.Next(3),
                    OperationId = operation.Activity.Id,
                    PayloadId = doc.Id,
                    Payload = JObject.FromObject(doc)
                };
                await _targetClient.UpsertObject(change);
                _logger.LogInformation("Change document is created. payloadId={0}, partitionKey={1}, operationId={2}",
                    change.PayloadId, change.PartitionKey, change.OperationId);
                return change;
            }
        }

    }
}
