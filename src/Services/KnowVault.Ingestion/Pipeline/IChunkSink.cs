using KnowVault.Contracts.Messages;
using KnowVault.Domain.Chunking;

namespace KnowVault.Ingestion.Pipeline;

/// <summary>A chunk plus its embedding, ready for the index and the chunk store.</summary>
public sealed record EmbeddedChunk(DocumentChunk Chunk, ReadOnlyMemory<float> Vector)
{
    public string ChunkId(DocumentChanged document) => $"{document.TenantId}-{document.DocumentId}-{Chunk.Index}";
}

/// <summary>
/// Destination for ingested chunks. The Azure implementation upserts to
/// AI Search (vectors + ACL filters) and Cosmos (full text + neighbor ids);
/// replacing a document deletes chunks the new version no longer has.
/// </summary>
public interface IChunkSink
{
    Task UpsertDocumentAsync(
        DocumentChanged document,
        string contentHash,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken);
}

/// <summary>Placeholder sink used until the AI Search + Cosmos resources exist: logs what would be written.</summary>
public sealed partial class LoggingChunkSink(ILogger<LoggingChunkSink> logger) : IChunkSink
{
    public Task UpsertDocumentAsync(
        DocumentChanged document,
        string contentHash,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken)
    {
        LogUpsert(logger, chunks.Count, document.DocumentId, document.TenantId, contentHash);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Would index {ChunkCount} chunks for document {DocumentId} (tenant {TenantId}, hash {ContentHash})")]
    private static partial void LogUpsert(
        ILogger logger, int chunkCount, string documentId, string tenantId, string contentHash);
}