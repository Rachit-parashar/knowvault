using System.Text.Json;

using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;

using KnowVault.Contracts.Messages;

using Microsoft.Extensions.DependencyInjection;

namespace KnowVault.Connector.Sync;

/// <summary>
/// The sync loop: every interval, list each source, diff against the stored
/// inventory (content hashes), and emit the difference — DocumentChanged for
/// new/edited items (content staged to the uploads container so the ingestion
/// path is identical to direct uploads) and DocumentDeleted tombstones for
/// removed items. Inventory persists as a blob per source, so restarts never
/// re-announce unchanged documents.
/// </summary>
public sealed partial class SyncEngine(
    IEnumerable<ISourceConnector> connectors,
    [FromKeyedServices("uploads")] BlobContainerClient uploads,
    [FromKeyedServices("sync-state")] BlobContainerClient syncState,
    ServiceBusClient messaging,
    IConfiguration configuration,
    ILogger<SyncEngine> logger) : BackgroundService
{
    private sealed record InventoryEntry(string DocumentId, string ContentHash);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Sync:IntervalSeconds", 30));
        var connectorCount = connectors.Count();
        LogStarted(logger, interval.TotalSeconds, connectorCount);

        using var timer = new PeriodicTimer(interval);
        do
        {
            foreach (var connector in connectors)
            {
                try
                {
                    await SyncSourceAsync(connector, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSyncFailed(logger, ex, connector.SourceId);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncSourceAsync(ISourceConnector connector, CancellationToken cancellationToken)
    {
        var inventory = await LoadInventoryAsync(connector.SourceId, cancellationToken);
        var items = await connector.ListAsync(cancellationToken);
        var seen = new HashSet<string>();
        var changes = 0;
        var sender = messaging.CreateSender("document-changed");
        var tombstoneSender = messaging.CreateSender("document-deleted");

        await using (sender.ConfigureAwait(false))
        await using (tombstoneSender.ConfigureAwait(false))
        {
            foreach (var item in items)
            {
                seen.Add(item.ExternalId);
                var known = inventory.GetValueOrDefault(item.ExternalId);
                if (known?.ContentHash == item.ContentHash)
                {
                    continue;
                }

                var documentId = known?.DocumentId ?? Guid.NewGuid().ToString("N");
                var blobPath = $"{item.TenantId}/{documentId}/{item.FileName}";

                var content = await connector.FetchAsync(item.ExternalId, cancellationToken);
                await uploads.GetBlobClient(blobPath).UploadAsync(content, overwrite: true, cancellationToken);

                var message = new DocumentChanged(
                    TenantId: item.TenantId,
                    SourceId: connector.SourceId,
                    DocumentId: documentId,
                    SourceType: "connector",
                    BlobPath: blobPath,
                    SourceUrl: item.SourceUrl,
                    ContentHash: item.ContentHash,
                    AllowedPrincipals: item.AllowedPrincipals,
                    DetectedAt: DateTimeOffset.UtcNow);
                await sender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(message))
                {
                    ContentType = "application/json",
                    Subject = MessageContracts.DocumentChangedV1,
                }, cancellationToken);

                inventory[item.ExternalId] = new InventoryEntry(documentId, item.ContentHash);
                changes++;
                LogChanged(logger, item.ExternalId, documentId, item.TenantId);
            }

            foreach (var (externalId, entry) in inventory.Where(kv => !seen.Contains(kv.Key)).ToList())
            {
                var tenantId = externalId.Split('/', 2)[0];
                var tombstone = new DocumentDeleted(tenantId, connector.SourceId, entry.DocumentId, DateTimeOffset.UtcNow);
                await tombstoneSender.SendMessageAsync(new ServiceBusMessage(BinaryData.FromObjectAsJson(tombstone))
                {
                    ContentType = "application/json",
                    Subject = MessageContracts.DocumentDeletedV1,
                }, cancellationToken);

                inventory.Remove(externalId);
                changes++;
                LogTombstoned(logger, externalId, entry.DocumentId);
            }
        }

        if (changes > 0)
        {
            await SaveInventoryAsync(connector.SourceId, inventory, cancellationToken);
            LogCycleDone(logger, connector.SourceId, changes);
        }
    }

    private async Task<Dictionary<string, InventoryEntry>> LoadInventoryAsync(
        string sourceId, CancellationToken cancellationToken)
    {
        try
        {
            var blob = await syncState.GetBlobClient($"{sourceId}.json").DownloadContentAsync(cancellationToken);
            return blob.Value.Content.ToObjectFromJson<Dictionary<string, InventoryEntry>>(Json) ?? [];
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }

    private async Task SaveInventoryAsync(
        string sourceId, Dictionary<string, InventoryEntry> inventory, CancellationToken cancellationToken) =>
        await syncState.GetBlobClient($"{sourceId}.json").UploadAsync(
            BinaryData.FromObjectAsJson(inventory, Json), overwrite: true, cancellationToken);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Sync engine started: {IntervalSeconds}s interval, {ConnectorCount} connector(s)")]
    private static partial void LogStarted(ILogger logger, double intervalSeconds, int connectorCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source item {ExternalId} changed -> document {DocumentId} (tenant {TenantId})")]
    private static partial void LogChanged(ILogger logger, string externalId, string documentId, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source item {ExternalId} removed -> tombstoning document {DocumentId}")]
    private static partial void LogTombstoned(ILogger logger, string externalId, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sync cycle for {SourceId}: {ChangeCount} change(s) emitted")]
    private static partial void LogCycleDone(ILogger logger, string sourceId, int changeCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sync cycle failed for source {SourceId}")]
    private static partial void LogSyncFailed(ILogger logger, Exception ex, string sourceId);
}