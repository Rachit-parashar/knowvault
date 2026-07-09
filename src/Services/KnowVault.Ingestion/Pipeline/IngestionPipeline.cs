using System.Security.Cryptography;
using System.Text;

using Azure.Storage.Blobs;

using KnowVault.Contracts.Messages;
using KnowVault.Domain.Chunking;
using KnowVault.Domain.Extraction;

namespace KnowVault.Ingestion.Pipeline;

/// <summary>
/// The per-document ingestion flow: download → hash (idempotency) → parse →
/// chunk → embed (batched) → index. Exceptions bubble to the Service Bus
/// handler, which abandons the message so retries and the DLQ do their job.
/// </summary>
public sealed partial class IngestionPipeline(
    BlobContainerClient uploads,
    IChunkEmbedder embedder,
    IChunkSink sink,
    IPdfExtractor pdfExtractor,
    ILogger<IngestionPipeline> logger)
{
    private const int EmbeddingBatchSize = 64;

    private static readonly MarkdownParser MarkdownParser = new();
    private static readonly PlainTextParser PlainTextParser = new();
    private static readonly DocumentChunker Chunker = new(new Cl100kTokenCounter());

    // Idempotency: in-memory fast path, then the sink's stored hash (Cosmos) —
    // durable across restarts, so unchanged documents are never re-embedded.
    private readonly Dictionary<string, string> _seenHashes = [];
    private readonly Lock _seenHashesLock = new();

    public async Task IngestAsync(DocumentChanged document, CancellationToken cancellationToken)
    {
        if (document.BlobPath is null)
        {
            throw new InvalidOperationException(
                $"Document {document.DocumentId} has no blob path; connector-sourced content arrives in Phase 5.");
        }

        var raw = await DownloadAsync(document.BlobPath, cancellationToken);
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(raw.ToMemory().Span));

        var documentKey = $"{document.TenantId}:{document.DocumentId}";
        lock (_seenHashesLock)
        {
            if (_seenHashes.TryGetValue(documentKey, out var previousHash) && previousHash == contentHash)
            {
                LogUnchanged(logger, document.DocumentId, contentHash);
                return;
            }
        }

        var storedHash = await sink.GetContentHashAsync(document.TenantId, document.DocumentId, cancellationToken);
        if (storedHash == contentHash)
        {
            lock (_seenHashesLock)
            {
                _seenHashes[documentKey] = contentHash;
            }

            LogUnchanged(logger, document.DocumentId, contentHash);
            return;
        }

        var fileName = document.BlobPath[(document.BlobPath.LastIndexOf('/') + 1)..];
        var extracted = await ExtractAsync(fileName, raw, cancellationToken);

        // Per-tenant chunking config comes from Admin later; defaults for now.
        var chunks = Chunker.Chunk(extracted, new ChunkingOptions());
        var embedded = await EmbedAllAsync(chunks, cancellationToken);

        await sink.UpsertDocumentAsync(document, contentHash, embedded, cancellationToken);

        lock (_seenHashesLock)
        {
            _seenHashes[documentKey] = contentHash;
        }

        LogIngested(logger, document.DocumentId, document.TenantId, chunks.Count, contentHash);
    }

    private async Task<BinaryData> DownloadAsync(string blobPath, CancellationToken cancellationToken)
    {
        var response = await uploads.GetBlobClient(blobPath).DownloadContentAsync(cancellationToken);
        return response.Value.Content;
    }

    /// <summary>PDFs go through Document Intelligence into markdown; text formats parse natively.</summary>
    private async Task<ExtractedDocument> ExtractAsync(
        string fileName, BinaryData raw, CancellationToken cancellationToken)
    {
        var title = Path.GetFileNameWithoutExtension(fileName);
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".pdf":
                var markdown = await pdfExtractor.ExtractMarkdownAsync(raw, cancellationToken);
                return MarkdownParser.Parse(markdown, title);
            case ".md" or ".markdown":
                return MarkdownParser.Parse(raw.ToString(), title);
            default:
                return PlainTextParser.Parse(raw.ToString(), title);
        }
    }

    private async Task<IReadOnlyList<EmbeddedChunk>> EmbedAllAsync(
        IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var result = new List<EmbeddedChunk>(chunks.Count);

        foreach (var batch in chunks.Chunk(EmbeddingBatchSize))
        {
            var vectors = await embedder.EmbedBatchAsync([.. batch.Select(c => c.EmbeddedText)], cancellationToken);
            result.AddRange(batch.Zip(vectors, (chunk, vector) => new EmbeddedChunk(chunk, vector)));
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Document {DocumentId} unchanged (hash {ContentHash}); skipping re-ingestion")]
    private static partial void LogUnchanged(ILogger logger, string documentId, string contentHash);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ingested document {DocumentId} for tenant {TenantId}: {ChunkCount} chunks (hash {ContentHash})")]
    private static partial void LogIngested(
        ILogger logger, string documentId, string tenantId, int chunkCount, string contentHash);
}