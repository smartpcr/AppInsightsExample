namespace Example.DocDb.Sync
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.DocDb;
    using Common.KeyVault;
    using Common.Instrumentation;
    using Microsoft.ApplicationInsights;
    using Microsoft.Azure.KeyVault;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.Azure.Documents;
    using System.Collections.Concurrent;
    using System.Threading.Tasks.Dataflow;
    using Microsoft.Azure.CosmosDB.BulkExecutor;
    using System.Linq;

    /// <summary>
    /// 
    /// </summary>
    public class DocDbBulkSyncWorker : BackgroundService
    {
        private readonly DocDbSettings _source;
        private readonly DocDbSettings _target;
        private readonly DataSyncSettings _syncSettings;
        private IDocumentDbClient _sourceClient;
        private IDocumentDbClient _targetClient;
        private readonly ILogger<DocDbBulkSyncWorker> _logger;
        private readonly TelemetryClient _telemetry;

        private readonly Metric _ru;
        private readonly Metric _docCount;
        private readonly Metric _latency;
        private readonly Metric _error;

        public DocDbBulkSyncWorker(
            IKeyVaultClient kvClient,
            IOptions<VaultSettings> vaultSettings,
            IOptionsSnapshot<DocDbSettings> source,
            IOptionsSnapshot<DocDbSettings> target,
            IOptions<DataSyncSettings> syncSettings,
            ILoggerFactory loggerFactory,
            TelemetryClient telemetry)
        {
            _source = source.Get("source");
            _sourceClient = new DocumentDbClient(kvClient, vaultSettings, _source, loggerFactory.CreateLogger<DocumentDbClient>(), telemetry);
            _target = target.Get("target");
            _targetClient = new DocumentDbClient(kvClient, vaultSettings, _target, loggerFactory.CreateLogger<DocumentDbClient>(), telemetry);
            _syncSettings = syncSettings.Value;
            _logger = loggerFactory.CreateLogger<DocDbBulkSyncWorker>();
            _telemetry = telemetry;

            _ru = _telemetry.GetMetric("bulk_executor_ru", "operation_name");
            _docCount = _telemetry.GetMetric("bulk_executor_docs", "operation_name");
            _latency = _telemetry.GetMetric("bulk_executor_latency", "operation_name");
            _error = _telemetry.GetMetric("bulk_executor_errors", "operation_name");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellation)
        {
            using (this.StartOperation(_telemetry))
            {
                var docsByType = await ReadDocuments(cancellation);
                await WriteDocuments(docsByType, cancellation);

                _logger.LogInformation("Done");
            }
        }

        public override void Dispose()
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

            base.Dispose();
        }

        private async Task<ConcurrentDictionary<string, List<Document>>> ReadDocuments(CancellationToken cancellation = default)
        {
            using (this.StartOperation(_telemetry))
            {
                _logger.LogInformation($"Started reading documents from {_source.Db}/{_source.Collection}");

                var docsByType = new ConcurrentDictionary<string, List<Document>>();
                var block = new ActionBlock<string>(
                    async (docType) =>
                    {
                        var query = new SqlQuerySpec(
                            "select * from c where c.documentType = @documentType",
                            new SqlParameterCollection(new[]
                            {
                                new SqlParameter("@documentType", docType)
                            }));
                        var documents = await _sourceClient.Query<Document>(query, cancel: cancellation);
                        var docs = documents.ToList();
                        docsByType.AddOrUpdate(docType, docs, (k, v) => docs);
                        _logger.LogInformation($"Read {docs.Count} documents for type {docType}.");
                        _docCount.TrackValue(docs.Count, $"read_{docType}");
                    },
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = _syncSettings.MaxDegreeOfParallelism,
                        CancellationToken = cancellation
                    });
                foreach (var docType in _syncSettings.DocumentTypes)
                {
                    block.Post(docType);
                }
                block.Complete();
                await block.Completion;

                _logger.LogInformation($"Total of {docsByType.Sum(kvp => kvp.Value.Count)} documents found.");

                return docsByType;
            }
        }

        private async Task WriteDocuments(ConcurrentDictionary<string, List<Document>> docsByType, CancellationToken cancellation = default)
        {
            using (this.StartOperation(_telemetry))
            {
                _logger.LogInformation($"Started writing documents to {_target.Db}/{_target.Collection}");

                // Set retry options high during initialization (default values).
                var client = _targetClient.Client;
                client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 30;
                client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 9;

                IBulkExecutor bulkExecutor = new BulkExecutor(client, _targetClient.Collection);
                await bulkExecutor.InitializeAsync();

                // Set retries to 0 to pass complete control to bulk executor.
                client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 0;
                client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 0;

                long totalDocumentsImported = 0;

                var block = new ActionBlock<string>(
                    async (docType) =>
                    {
                        var docs = docsByType[docType].Select(d => { d.Id = null; return d; }).ToList();
                        var response = await bulkExecutor.BulkImportAsync(
                            docs, 
                            enableUpsert: false,
                            disableAutomaticIdGeneration: false, cancellationToken: cancellation);

                        _logger.LogInformation($"Wrote {response.NumberOfDocumentsImported} documents for type {docType}.");
                        _ru.TrackValue(response.TotalRequestUnitsConsumed, $"import_{docType}");
                        _latency.TrackValue(response.TotalTimeTaken.TotalMilliseconds, $"import_{docType}");
                        _docCount.TrackValue(response.NumberOfDocumentsImported, $"import_{docType}");
                        _error.TrackValue(response.BadInputDocuments, $"import_{docType}");
                        
                        Interlocked.Add(ref totalDocumentsImported, response.NumberOfDocumentsImported);
                    },
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = _syncSettings.MaxDegreeOfParallelism
                    });

                foreach (var docType in _syncSettings.DocumentTypes)
                {
                    block.Post(docType);
                }
                block.Complete();
                await block.Completion;

                _logger.LogInformation($"Total of {totalDocumentsImported} documents written to target db.");
            }
        }
    }
}