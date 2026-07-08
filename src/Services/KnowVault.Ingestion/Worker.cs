namespace KnowVault.Ingestion;

/// <summary>
/// Phase 1 turns this into the ingestion pipeline: consume DocumentChanged
/// messages from Service Bus and run extract → chunk → embed → index,
/// dead-lettering poison messages.
/// </summary>
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion worker started; waiting for Service Bus wiring (Phase 1)")]
    private static partial void LogStarted(ILogger logger);
}