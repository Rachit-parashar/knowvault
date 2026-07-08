namespace KnowVault.Connector;

/// <summary>
/// Phase 5 turns this into source sync: pull documents + ACLs via Graph
/// delta queries, detect changes by content hash, and emit
/// DocumentChanged / DocumentDeleted messages.
/// </summary>
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connector worker started; source sync lands in Phase 5 (direct upload covers Phases 1-4)")]
    private static partial void LogStarted(ILogger logger);
}