using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.DocDb;
using Common.Instrumentation;
using Common.KeyVault;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.CosmosDB.BulkExecutor;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Example.DocDb.Sync
{
    public class CopyCollectionsWorker : BackgroundService
    {
        private readonly CopyCollectionsSettings _settings;
        private readonly ILogger<CopyCollectionsWorker> _logger;
        private readonly TelemetryClient _telemetry;
        private IDocumentDbClient _sourceClient;
        private IDocumentDbClient _targetClient;
        
        private readonly Metric _ru;
        private readonly Metric _docCount;
        private readonly Metric _latency;
        private readonly Metric _error;

        public CopyCollectionsWorker(
            IKeyVaultClient kvClient,
            IOptions<VaultSettings> vaultSettings,
            IOptions<CopyCollectionsSettings> settings,
            ILoggerFactory loggerFactory,
            TelemetryClient telemetry)
        {
            _settings = settings.Value;
            _logger = loggerFactory.CreateLogger<CopyCollectionsWorker>();
            _telemetry = telemetry;

            _sourceClient = new DocumentDbClient(kvClient, vaultSettings, _settings.Source,
                loggerFactory.CreateLogger<DocumentDbClient>(), telemetry);
            _targetClient = new DocumentDbClient(kvClient, vaultSettings, _settings.Target,
                loggerFactory.CreateLogger<DocumentDbClient>(), telemetry);
            
            _ru = _telemetry.GetMetric("bulk_executor_ru", "collection_name");
            _docCount = _telemetry.GetMetric("bulk_executor_docs", "collection_name");
            _latency = _telemetry.GetMetric("bulk_executor_latency", "collection_name");
            _error = _telemetry.GetMetric("bulk_executor_errors", "collection_name");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (this.StartOperation(_telemetry))
            {
                foreach (var collection in _settings.Collections)
                {
                    _logger.LogInformation("Synchtonizing collection {0}", collection);
                    var totalDocumentsCopied = await CopyDocuments(collection, stoppingToken);
                    _logger.LogInformation("Total of {0} documents copied", totalDocumentsCopied);
                }
                
                _logger.LogInformation("Done!");
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

        private async Task<int> CopyDocuments(string collectionName, CancellationToken stoppingToken)
        {
            using (this.StartOperation(_telemetry))
            {
                await _sourceClient.SwitchCollection(collectionName);

                var total = await _sourceClient.CountAsync(stoppingToken);
                _logger.LogInformation("Waiting to copy total of {0} documents from source collection", total);
                if (total == 0)
                {
                    return 0;
                }

                int totalDocumentRead = 0;
                int totalDocumentsWrite = 0;
                try
                {
                    var query = new SqlQuerySpec("select * from c");
                    var feedOps = new FeedOptions()
                    {
                        EnableCrossPartitionQuery = true,
                        PopulateQueryMetrics = true
                    };
                    
                    var responses = await _sourceClient.QueryInBatches<Document>(query, feedOps, stoppingToken);
                    while (responses?.Count > 0)
                    {
                        var docs = responses.ToList();
                        totalDocumentRead += docs.Count;
                        _logger.LogInformation("Read {0} of {1} documents from {2}", totalDocumentRead, total,
                            collectionName);

                        var docsWritten = await WriteDocuments(collectionName, docs, stoppingToken);
                        totalDocumentsWrite += docsWritten;
                        _logger.LogInformation("Write {0} documents to {1}", totalDocumentsWrite, collectionName);

                        feedOps.RequestContinuation = responses.ResponseContinuation;
                        if (feedOps.RequestContinuation != null)
                        {
                            responses = await _sourceClient.QueryInBatches<Document>(query, feedOps, stoppingToken);
                        }
                        else
                        {
                            responses = null;
                        }
                    }

                    return totalDocumentsWrite;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed sync collection {0}", collectionName);
                    return totalDocumentsWrite;
                }
            }
        }
        
        private async Task<int> WriteDocuments(string collectionName, List<Document> docs, CancellationToken stoppingToken = default)
        {
            using (this.StartOperation(_telemetry))
            {
                var partitionKeyPaths = _sourceClient.Collection.PartitionKey.Paths?.ToArray();
                await _targetClient.SwitchCollection(collectionName, partitionKeyPaths);
                
                // Set retry options high during initialization (default values).
                var client = _targetClient.Client;
                client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 30;
                client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 9;

                IBulkExecutor bulkExecutor = new BulkExecutor(client, _targetClient.Collection);
                await bulkExecutor.InitializeAsync();

                // Set retries to 0 to pass complete control to bulk executor.
                client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 0;
                client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 0;

                var response = await bulkExecutor.BulkImportAsync(
                    docs, 
                    enableUpsert: true,
                    disableAutomaticIdGeneration: false, 
                    cancellationToken: stoppingToken);

                _logger.LogInformation($"Wrote {response.NumberOfDocumentsImported} documents");
                _ru.TrackValue(response.TotalRequestUnitsConsumed, $"import_{collectionName}");
                _latency.TrackValue(response.TotalTimeTaken.TotalMilliseconds, $"import_{collectionName}");
                _docCount.TrackValue(response.NumberOfDocumentsImported, $"import_{collectionName}");
                _error.TrackValue(response.BadInputDocuments?.Count??0, $"import_{collectionName}");
                        
                _logger.LogInformation($"Total of {response.NumberOfDocumentsImported} documents written to {collectionName}.");

                return (int)response.NumberOfDocumentsImported;
            }
        }
    }
}