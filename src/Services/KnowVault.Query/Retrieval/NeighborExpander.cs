using KnowVault.Contracts.Retrieval;

using Microsoft.Azure.Cosmos;

namespace KnowVault.Query.Retrieval;

/// <summary>
/// Context expansion (plan §6 step 4): the top hit's adjacent chunks are
/// point-read from Cosmos and appended, so the model sees the passage's
/// surroundings instead of a fragment. Only the top hit is expanded — cheap
/// (two point reads) and where the benefit concentrates.
/// </summary>
public sealed partial class NeighborExpander(CosmosClient cosmosClient, ILogger<NeighborExpander> logger)
{
    private readonly Container _chunks = cosmosClient.GetContainer("knowvault", "chunks");

    private sealed record ChunkRecord(string Id, string TenantId, string DocumentId, int ChunkIndex, string Breadcrumb, string Content);

    public async Task<IReadOnlyList<RetrievedChunk>> ExpandTopHitAsync(
        string tenantId, IReadOnlyList<RetrievedChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return chunks;
        }

        var top = chunks[0];
        var marker = top.ChunkId.LastIndexOf('-');
        if (marker < 0 || !int.TryParse(top.ChunkId[(marker + 1)..], out var index))
        {
            return chunks;
        }

        var existing = chunks.Select(c => c.ChunkId).ToHashSet(StringComparer.Ordinal);
        var expanded = new List<RetrievedChunk>(chunks);
        var added = 0;

        foreach (var neighborIndex in (int[])[index - 1, index + 1])
        {
            if (neighborIndex < 0)
            {
                continue;
            }

            var neighborId = $"{top.ChunkId[..marker]}-{neighborIndex}";
            if (existing.Contains(neighborId))
            {
                continue;
            }

            try
            {
                var record = await _chunks.ReadItemAsync<ChunkRecord>(
                    neighborId, new PartitionKey(tenantId), cancellationToken: cancellationToken);
                expanded.Add(new RetrievedChunk(
                    neighborId, top.DocumentId, record.Resource.Breadcrumb, record.Resource.Content,
                    top.SourceUrl, Score: 0));
                added++;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Document edge — no neighbor on this side.
            }
        }

        if (added > 0)
        {
            LogExpanded(logger, added, top.ChunkId);
        }

        return expanded;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Neighbor expansion added {Count} chunk(s) around {ChunkId}")]
    private static partial void LogExpanded(ILogger logger, int count, string chunkId);
}