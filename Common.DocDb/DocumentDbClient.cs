using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Instrumentation;
using Common.KeyVault;
using EnsureThat;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Common.DocDb
{
    


    /// <summary>
    /// 
    /// </summary>
    public sealed class DocumentDbClient : IDocumentDbClient
    {
        private readonly DocDbSettings _settings;
        private readonly ILogger<DocumentDbClient> _logger;
        private readonly TelemetryClient _telemetry;
        private readonly FeedOptions _feedOptions;

        private readonly Metric _docCounter;
        private readonly Metric _retries;
        private readonly Metric _requestCharges;
        private readonly Metric _latency;
        private readonly Metric _errors;

        public DocumentCollection Collection { get; private set; }
        public DocumentClient Client { get; }

        public DocumentDbClient(
            IKeyVaultClient kvClient,
            IOptions<VaultSettings> vaultSettings,
            DocDbSettings dbSettings,
            ILogger<DocumentDbClient> logger,
            TelemetryClient telemetry)
        {
            _settings = dbSettings;
            _logger = logger;
            _telemetry = telemetry;

            _logger.LogInformation($"Retrieving auth key '{_settings.AuthKeySecret}' from vault '{vaultSettings.Value.Name}'");
            var authKey = kvClient.GetSecretAsync(
                vaultSettings.Value.VaultUrl,
                _settings.AuthKeySecret).GetAwaiter().GetResult();
            Client = new DocumentClient(
                _settings.AccountUri,
                authKey.Value,
                desiredConsistencyLevel: ConsistencyLevel.Session,
                serializerSettings: new JsonSerializerSettings()
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                });

            var database = Client.CreateDatabaseQuery().Where(db => db.Id == _settings.Db).AsEnumerable().First();
            Collection = Client.CreateDocumentCollectionQuery(database.SelfLink).Where(c => c.Id == _settings.Collection).AsEnumerable().First();
            _feedOptions = new FeedOptions() { PopulateQueryMetrics = _settings.CollectMetrics };

            _docCounter = _telemetry.GetMetric("docdb_count", "operation_name");
            _retries = _telemetry.GetMetric("docdb_retries", "operation_name");
            _requestCharges = _telemetry.GetMetric("docdb_ru", "operation_name");
            _latency = _telemetry.GetMetric("docdb_latency", "operation_name");
            _errors = _telemetry.GetMetric("docdb_error", "operation_name");

            _logger.LogInformation($"Connected to doc db '{Collection.SelfLink}'");
        }

        public async Task SwitchCollection(string collectionName, params string[] partitionKeyPaths)
        {
            if (Collection.Id == collectionName)
            {
                return;
            }
            
            var database = Client.CreateDatabaseQuery().Where(db => db.Id == _settings.Db).AsEnumerable().First();
            Collection = Client.CreateDocumentCollectionQuery(database.SelfLink).Where(c => c.Id == collectionName).AsEnumerable().FirstOrDefault();
            if (Collection == null)
            {
                var partition = new PartitionKeyDefinition();
                if (partitionKeyPaths?.Any() == true)
                {
                    foreach (var keyPath in partitionKeyPaths)
                    {
                        partition.Paths.Add(keyPath);
                    }
                }
                else
                {
                    partition.Paths.Add("/id");
                }

                try
                {
                    await Client.CreateDocumentCollectionAsync(database.SelfLink, new DocumentCollection()
                    {
                        Id = collectionName,
                        PartitionKey = partition
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create collection");
                    throw;
                }
                
                Collection = Client.CreateDocumentCollectionQuery(database.SelfLink).Where(c => c.Id == collectionName).AsEnumerable().FirstOrDefault();
                _logger.LogInformation("Created collection {0} in {1}/{2}", collectionName, _settings.Account, _settings.Db);
            }

            _logger.LogInformation("Switched to collection {0}", collectionName);
        }

        public async Task<int> CountAsync(CancellationToken cancel = default)
        {
            var countQuery = @"SELECT VALUE COUNT(1) FROM c";
            var result = Client.CreateDocumentQuery<int>(
                Collection.DocumentsLink,
                new SqlQuerySpec()
                {
                    QueryText = countQuery,
                    Parameters = new SqlParameterCollection()
                },
                new FeedOptions()
                {
                    EnableCrossPartitionQuery = true
                }).AsDocumentQuery();

            int count = 0;
            while (result.HasMoreResults)
            {
                var batchSize = await result.ExecuteNextAsync<int>(cancel);
                count += batchSize.First();
            }
            return count;
        }

        public async Task DeleteObject(string id, CancellationToken cancel = default)
        {
            Ensure.That(id).IsNotNullOrWhiteSpace();

            using (var operation = this.StartOperation(_telemetry))
            {
                try
                {
                    Uri docUri = UriFactory.CreateDocumentUri(_settings.Db, _settings.Collection, id);
                    var response = await Client.DeleteDocumentAsync(docUri, cancellationToken: cancel);
                    _requestCharges.TrackValue(response.RequestCharge, operation.OperationName);
                    _latency.TrackValue(response.RequestLatency.TotalMilliseconds, operation.OperationName);
                    _docCounter.TrackValue(1, operation.OperationName);
                    //_logger.LogInformation("Total RU for {0}: {1}", nameof(DeleteObject), response.RequestCharge);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to Delete document. DatabaseName={0}, CollectionName={1}, DocumentId={2}, Exception={3}",
                        _settings.Db, _settings.Collection, id);
                    _errors.TrackValue(1, operation.OperationName);
                    throw;
                }
            }
        }

        public async Task<IEnumerable<T>> Query<T>(SqlQuerySpec querySpec, FeedOptions feedOptions = null, CancellationToken cancel = default)
        {
            Ensure.That(querySpec).IsNotNull();

            using (var operation = this.StartOperation(_telemetry))
            {
                try
                {
                    var output = new List<T>();
                    feedOptions = feedOptions ?? _feedOptions;
                    feedOptions.PopulateQueryMetrics = _feedOptions.PopulateQueryMetrics;
                    var query = Client
                        .CreateDocumentQuery<T>(Collection.SelfLink, querySpec, feedOptions)
                        .AsDocumentQuery();

                    while (query.HasMoreResults)
                    {
                        var response = await query.ExecuteNextAsync<T>(cancel);
                        output.AddRange(response);

                        _requestCharges.TrackValue(response.RequestCharge, operation.OperationName);
                        _docCounter.TrackValue(response.Count, operation.OperationName);

                        if (_settings.CollectMetrics)
                        {
                            var queryMetrics = response.QueryMetrics;
                            foreach (var label in queryMetrics.Keys)
                            {
                                var queryMetric = queryMetrics[label];
                                _retries.TrackValue(queryMetric.Retries, operation.OperationName);
                                _latency.TrackValue(queryMetric.TotalTime.TotalMilliseconds, operation.OperationName);
                            }
                        }
                    }
                    //_logger.LogInformation("Total RU for {0}: {1}", nameof(Query), totalRequestUnits);

                    return output;
                }
                catch (DocumentClientException e)
                {
                    _logger.LogError(
                        e,
                        $"Unable to Query. DatabaseName={_settings.Db}, CollectionName={_settings.Collection}, Query={querySpec}, FeedOptions={feedOptions}");

                    throw;
                }
            }
                
        }

        public async Task<FeedResponse<T>> QueryInBatches<T>(SqlQuerySpec querySpec, FeedOptions feedOptions = null,
            CancellationToken cancel = default)
        {
            Ensure.That(querySpec).IsNotNull();

            using (var operation = this.StartOperation(_telemetry))
            {
                try
                {
                    feedOptions = feedOptions ?? _feedOptions;
                    feedOptions.PopulateQueryMetrics = _feedOptions.PopulateQueryMetrics;
                    var query = Client
                        .CreateDocumentQuery<T>(Collection.SelfLink, querySpec, feedOptions)
                        .AsDocumentQuery();

                    if (query.HasMoreResults)
                    {
                        var response = await query.ExecuteNextAsync<T>(cancel);
                    
                        _requestCharges.TrackValue(response.RequestCharge, operation.OperationName);
                        _docCounter.TrackValue(response.Count, operation.OperationName);

                        if (_settings.CollectMetrics)
                        {
                            var queryMetrics = response.QueryMetrics;
                            foreach (var label in queryMetrics.Keys)
                            {
                                var queryMetric = queryMetrics[label];
                                _retries.TrackValue(queryMetric.Retries, operation.OperationName);
                                _latency.TrackValue(queryMetric.TotalTime.TotalMilliseconds, operation.OperationName);
                            }
                        }
                    
                        return response;
                    }

                    return null;
                }
                catch (DocumentClientException e)
                {
                    _logger.LogError(
                        e,
                        $"Unable to Query. DatabaseName={_settings.Db}, CollectionName={_settings.Collection}, Query={querySpec}, FeedOptions={feedOptions}");

                    throw;
                }
            }
        }

        public async Task<T> ReadObject<T>(string id, CancellationToken cancel = default)
        {
            using (var operation = this.StartOperation(_telemetry))
            {
                try
                {
                    Uri docUri = UriFactory.CreateDocumentUri(_settings.Db, _settings.Collection, id);
                    var response = await Client.ReadDocumentAsync<T>(docUri, cancellationToken: cancel);
                    _requestCharges.TrackValue(response.RequestCharge, operation.OperationName);
                    _latency.TrackValue(response.RequestLatency.TotalMilliseconds, operation.OperationName);
                    _docCounter.TrackValue(1, operation.OperationName);
                    //_logger.LogInformation("Total RU for {0}: {1}", nameof(ReadObject), response.RequestCharge);
                    return response;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to Read document. DatabaseName={0}, CollectionName={1}, DocumentId={2}, Exception={3}",
                        _settings.Db, _settings.Collection, id);
                    _errors.TrackValue(1, operation.OperationName);
                    throw;
                }
            }
        }

        public async Task<string> UpsertObject<T>(T @object, RequestOptions requestOptions = null, CancellationToken cancel = default)
        {
            using (var operation = this.StartOperation(_telemetry))
            {
                try
                {
                    var response = await Client.UpsertDocumentAsync(Collection.SelfLink, @object, requestOptions, cancellationToken: cancel);
                    _requestCharges.TrackValue(response.RequestCharge, operation.OperationName);
                    _latency.TrackValue(response.RequestLatency.TotalMilliseconds, operation.OperationName);
                    _docCounter.TrackValue(1, operation.OperationName);
                    //_logger.LogInformation("Total RU for {0}: {1}", nameof(UpsertObject), response.RequestCharge);
                    return response.Resource.Id;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Unable to Upsert object. CollectionUrl={0}",
                        Collection.SelfLink);
                    _errors.TrackValue(1, operation.OperationName);
                    throw;
                }
            }
        }

        #region IDisposable Support
        private bool isDisposed; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    Client.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                isDisposed = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~DocDb() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}