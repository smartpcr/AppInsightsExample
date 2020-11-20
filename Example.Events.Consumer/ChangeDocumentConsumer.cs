namespace Example.Events.Consumer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.DocDb;
    using Common.KeyVault;
    using Microsoft.ApplicationInsights;
    using Microsoft.Azure.Documents.ChangeFeedProcessor.FeedProcessing;
    using Microsoft.Azure.Documents.ChangeFeedProcessor.PartitionManagement;
    using Microsoft.Azure.KeyVault;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using ChangeFeedProcessorBuilder = Microsoft.Azure.Documents.ChangeFeedProcessor.ChangeFeedProcessorBuilder;
    using DocumentCollectionInfo = Microsoft.Azure.Documents.ChangeFeedProcessor.DocumentCollectionInfo;


    /// <summary>
    /// 
    /// </summary>
    public class ChangeDocumentConsumer : BackgroundService, IChangeFeedObserverFactory
    {
        private readonly DocumentCollectionInfo _feedCollection;
        private readonly DocumentCollectionInfo _leaseCollection;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<ChangeDocumentConsumer> _logger;
        private readonly TelemetryClient _telemetry;
        private IChangeFeedProcessor _processor;

        public ChangeDocumentConsumer(
            IKeyVaultClient kvClient,
            IOptions<VaultSettings> vaultSettings,
            IOptionsSnapshot<DocDbSettings> changeDb,
            IOptionsSnapshot<DocDbSettings> leaseDb,
            ILoggerFactory loggerFactory,
            TelemetryClient telemetry)
        {
            var changeDbSettings = changeDb.Get("ChangeDb");
            var changeDbAuthKey = kvClient.GetSecretAsync(
                vaultSettings.Value.VaultUrl,
                changeDbSettings.AuthKeySecret).GetAwaiter().GetResult();
            _feedCollection = new DocumentCollectionInfo()
            {
                DatabaseName = changeDbSettings.Db,
                CollectionName = changeDbSettings.Collection,
                Uri = changeDbSettings.AccountUri,
                MasterKey = changeDbAuthKey.Value
            };

            var leaseDbSettings = leaseDb.Get("LeaseDb");
            var leaseDbAuthKey = kvClient.GetSecretAsync(
                vaultSettings.Value.VaultUrl,
                leaseDbSettings.AuthKeySecret).GetAwaiter().GetResult();
            _leaseCollection = new DocumentCollectionInfo()
            {
                DatabaseName = leaseDbSettings.Db,
                CollectionName = leaseDbSettings.Collection,
                Uri = leaseDbSettings.AccountUri,
                MasterKey = leaseDbAuthKey.Value
            };

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<ChangeDocumentConsumer>();
            _telemetry = telemetry;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var hostName = Environment.MachineName;
            var builder = new ChangeFeedProcessorBuilder()
                .WithHostName(hostName)
                .WithFeedCollection(_feedCollection)
                .WithLeaseCollection(_leaseCollection)
                .WithObserverFactory(this);
            
            _processor = await builder.BuildAsync();
            await _processor.StartAsync();
            _logger.LogInformation($"Change feed processor started at {hostName}...");

            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _processor?.StopAsync();

            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ping");
            await Task.Delay(1000);
        }

        #region IChangeFeedObserver
        public IChangeFeedObserver CreateObserver()
        {
            return new DocumentChangeObserver(
                _loggerFactory.CreateLogger<DocumentChangeObserver>(),
                _telemetry);
        }
        #endregion
    }
}