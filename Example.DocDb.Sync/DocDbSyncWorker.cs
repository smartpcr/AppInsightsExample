using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Common.DocDb;
using Common.Instrumentation;
using Common.KeyVault;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Documents;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Example.DocDb.Sync
{
    public class DocDbSyncWorker : BackgroundService, IDisposable
    {
        private readonly DocDbSettings _source;
        private readonly DocDbSettings _target;
        private readonly DataSyncSettings _syncSettings;
        private IDocumentDbClient _sourceClient;
        private IDocumentDbClient _targetClient;
        private ILogger<DocDbSyncWorker> _logger;
        private TelemetryClient _telemetry;

        public DocDbSyncWorker(
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
            _logger = loggerFactory.CreateLogger<DocDbSyncWorker>();
            _telemetry = telemetry;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellation)
        {
            using (var operation = this.StartOperation(_telemetry))
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
            using (var operation = this.StartOperation(_telemetry))
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
                        var documents = await _sourceClient.Query<Document>(query);
                        var docs = documents.ToList();
                        docsByType.AddOrUpdate(docType, docs, (k, v) => docs);
                        _logger.LogInformation($"Read {docs.Count} documents for type {docType}.");
                    },
                    new ExecutionDataflowBlockOptions()
                    {
                        MaxDegreeOfParallelism = _syncSettings.MaxDegreeOfParallelism,
                        CancellationToken =cancellation
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
            using (var operation = this.StartOperation(_telemetry))
            {
                _logger.LogInformation($"Started writing documents to {_target.Db}/{_target.Collection}");

                var block = new ActionBlock<string>(
                    async (docType) =>
                    {
                        var docs = docsByType[docType];
                        int totalDocumentsWrote = 0;
                        foreach (var doc in docs)
                        {
                            doc.Id = null;
                            await _targetClient.UpsertObject<Document>(doc);
                            totalDocumentsWrote++;
                        }

                        _logger.LogInformation($"Wrote {totalDocumentsWrote} documents for type {docType}.");
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

                _logger.LogInformation($"Total of {docsByType.Sum(kvp => kvp.Value.Count)} documents written to target db.");
            }
        }
    }
}
