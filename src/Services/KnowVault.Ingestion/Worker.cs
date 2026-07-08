using Azure.Messaging.ServiceBus;

using KnowVault.Contracts.Messages;
using KnowVault.Ingestion.Pipeline;

namespace KnowVault.Ingestion;

/// <summary>
/// Competing-consumer worker on the document-changed queue. Failures abandon
/// the message; after MaxDeliveryCount attempts Service Bus dead-letters it,
/// where the requeue CLI can inspect and replay.
/// </summary>
public sealed partial class Worker(
    ServiceBusClient messaging,
    IngestionPipeline pipeline,
    ILogger<Worker> logger) : BackgroundService
{
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = messaging.CreateProcessor("document-changed", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 4,
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        LogStarted(logger);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var document = args.Message.Body.ToObjectFromJson<DocumentChanged>();
        if (document is null)
        {
            LogPoisonMessage(logger, args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "deserialization-failed",
                $"Body is not a {nameof(DocumentChanged)} payload.", args.CancellationToken);
            return;
        }

        try
        {
            await pipeline.IngestAsync(document, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogIngestionFailed(logger, ex, document.DocumentId, args.Message.DeliveryCount);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(logger, args.Exception, args.ErrorSource.ToString());
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion worker listening on document-changed")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} is not deserializable; dead-lettering")]
    private static partial void LogPoisonMessage(ILogger logger, string messageId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Ingestion failed for document {DocumentId} (delivery {DeliveryCount}); abandoning for retry")]
    private static partial void LogIngestionFailed(ILogger logger, Exception ex, string documentId, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus processor error from {ErrorSource}")]
    private static partial void LogProcessorError(ILogger logger, Exception ex, string errorSource);
}