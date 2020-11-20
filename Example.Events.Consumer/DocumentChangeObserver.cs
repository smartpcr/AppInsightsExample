namespace Example.Events.Consumer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Instrumentation;
    using Example.Events.Producer;
    using Microsoft.ApplicationInsights;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.ChangeFeedProcessor.FeedProcessing;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;


    /// <summary>
    /// 
    /// </summary>
    public class DocumentChangeObserver : IChangeFeedObserver
    {
        private readonly ILogger<DocumentChangeObserver> _logger;
        private readonly TelemetryClient _telemetry;

        private readonly Metric _onClose;
        private readonly Metric _onOpen;
        private readonly Metric _onProcess;
        private readonly Metric _onFail;
        private readonly Metric _onSuccess;
        private readonly Metric _delay;

        public DocumentChangeObserver(
            ILogger<DocumentChangeObserver> logger,
            TelemetryClient telemetry)
        {
            _logger = logger;
            _telemetry = telemetry;

            _onOpen = _telemetry.GetMetric("observer_on_open", "partitions");
            _onClose = _telemetry.GetMetric("observer_on_close", "partitions");
            _onProcess = _telemetry.GetMetric("observer_on_process", "partitions");
            _onFail = _telemetry.GetMetric("observer_on_fail", "partitions");
            _onSuccess = _telemetry.GetMetric("observer_on_success", "partitions");
            _delay = _telemetry.GetMetric("observer_delay", "partition_key");
        }

        public Task CloseAsync(IChangeFeedObserverContext context, ChangeFeedObserverCloseReason reason)
        {
            _onClose.TrackValue(1, context.PartitionKeyRangeId);
            _logger.LogInformation("Document change observer stopped: {0}", context.PartitionKeyRangeId);
            return Task.CompletedTask;
        }

        public Task OpenAsync(IChangeFeedObserverContext context)
        {
            _onOpen.TrackValue(1, context.PartitionKeyRangeId);
            _logger.LogInformation("Document change observer started...{0}", context.PartitionKeyRangeId);
            return Task.CompletedTask;
        }

        public async Task ProcessChangesAsync(IChangeFeedObserverContext context, IReadOnlyList<Document> docs, CancellationToken cancellationToken)
        {
            using (var operation = this.StartOperation(_telemetry))
            {
                _onProcess.TrackValue(1, context.PartitionKeyRangeId);
                
                await Task.WhenAll(docs.Select(async doc => {
                    try
                    {
                        var changeDoc = JsonConvert.DeserializeObject<ChangeDocument>(doc.ToString());
                        _logger.LogInformation("Processing document: id={0}, partition_key={1}, operation_id={2}",
                            changeDoc.PayloadId, changeDoc.PartitionKey, changeDoc.OperationId);
                        var delay = DateTime.UtcNow - changeDoc.Timestamp;
                        _delay.TrackValue(delay.TotalMilliseconds, changeDoc.PartitionKey);
                        await ProcessChangeDocument(changeDoc, cancellationToken);
                        _onSuccess.TrackValue(1, context.PartitionKeyRangeId);
                    }
                    catch (Exception e)
                    {
                        _onFail.TrackValue(1, context.PartitionKeyRangeId);
                        _telemetry.TrackException(e);
                        _logger.LogError(e, "failed processing document change: partition={0}", context.PartitionKeyRangeId);
                    }
                }));
            }
        }

        private async Task ProcessChangeDocument(ChangeDocument changeDocument, CancellationToken cancel)
        {
            var random = new Random(DateTime.Now.Millisecond);
            using (var operation = this.StartOperation(_telemetry, changeDocument.OperationId))
            {
                await Task.Delay(random.Next(10000), cancel);
            }
        }
    }
}