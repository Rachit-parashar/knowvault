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
    IChunkSink sink,
    ILogger<Worker> logger) : BackgroundService
{
    private ServiceBusProcessor? _processor;
    private ServiceBusProcessor? _deleteProcessor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = messaging.CreateProcessor("document-changed", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 4,
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        _deleteProcessor = messaging.CreateProcessor("document-deleted", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false,
        });

        _deleteProcessor.ProcessMessageAsync += HandleDeleteMessageAsync;
        _deleteProcessor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        await _deleteProcessor.StartProcessingAsync(stoppingToken);
        LogStarted(logger);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task HandleDeleteMessageAsync(ProcessMessageEventArgs args)
    {
        DocumentDeleted? tombstone;
        try
        {
            tombstone = args.Message.Body.ToObjectFromJson<DocumentDeleted>();
        }
        catch (System.Text.Json.JsonException)
        {
            tombstone = null;
        }

        if (tombstone is not { TenantId: not null, DocumentId: not null })
        {
            await args.DeadLetterMessageAsync(args.Message, "deserialization-failed",
                $"Body is not a {nameof(DocumentDeleted)} payload.", args.CancellationToken);
            return;
        }

        try
        {
            await sink.DeleteDocumentAsync(tombstone.TenantId, tombstone.DocumentId, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            LogTombstoned(logger, tombstone.DocumentId, tombstone.TenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogIngestionFailed(logger, ex, tombstone.DocumentId, args.Message.DeliveryCount);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var document = Parse(args.Message.Body);
        if (document is null)
        {
            LogPoisonMessage(logger, args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "deserialization-failed",
                $"Body is neither a {nameof(DocumentChanged)} payload nor an Event Grid BlobCreated event.",
                args.CancellationToken);
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

    /// <summary>Accepts both message shapes: our contract (Admin/Connector) and Event Grid (Azure upload path).</summary>
    private static DocumentChanged? Parse(BinaryData body)
    {
        if (EventGridBlobCreated.TryMap(body, out var mapped))
        {
            return mapped;
        }

        try
        {
            var document = body.ToObjectFromJson<DocumentChanged>();
            // Reject bodies that deserialized but carry none of the contract's identity.
            return document is { TenantId: not null, DocumentId: not null } ? document : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(logger, args.Exception, args.ErrorSource.ToString());
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in new[] { _processor, _deleteProcessor })
        {
            if (processor is not null)
            {
                await processor.StopProcessingAsync(cancellationToken);
                await processor.DisposeAsync();
            }
        }

        await base.StopAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion worker listening on document-changed and document-deleted")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tombstoned document {DocumentId} (tenant {TenantId}): all chunks removed")]
    private static partial void LogTombstoned(ILogger logger, string documentId, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} is not deserializable; dead-lettering")]
    private static partial void LogPoisonMessage(ILogger logger, string messageId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Ingestion failed for document {DocumentId} (delivery {DeliveryCount}); abandoning for retry")]
    private static partial void LogIngestionFailed(ILogger logger, Exception ex, string documentId, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus processor error from {ErrorSource}")]
    private static partial void LogProcessorError(ILogger logger, Exception ex, string errorSource);
}