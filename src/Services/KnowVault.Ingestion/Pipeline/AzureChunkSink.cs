using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

using KnowVault.Contracts.Messages;

using Microsoft.Azure.Cosmos;

namespace KnowVault.Ingestion.Pipeline;

/// <summary>
/// The real chunk destination: Azure AI Search (hybrid retrieval + the
/// allowedPrincipals security-trimming field) and Cosmos DB (full chunk
/// records for answer-time point reads and neighbor expansion).
/// Replacing a document also deletes chunks the new version no longer has,
/// so shortened documents don't leave stale content behind.
/// </summary>
public sealed partial class AzureChunkSink(
    SearchIndexClient searchIndexClient,
    CosmosClient cosmosClient,
    ILogger<AzureChunkSink> logger) : IChunkSink, IDisposable
{
    public const string IndexName = "chunks";
    private const int VectorDimensions = 3072;

    private readonly SearchClient _searchClient = searchIndexClient.GetSearchClient(IndexName);
    private readonly Container _chunks = cosmosClient.GetContainer("knowvault", "chunks");
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private volatile bool _indexEnsured;

    public async Task UpsertDocumentAsync(
        DocumentChanged document,
        string contentHash,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken)
    {
        await EnsureIndexAsync(cancellationToken);

        var updatedAt = DateTimeOffset.UtcNow;

        if (chunks.Count > 0)
        {
            var searchDocuments = chunks.Select(c => new SearchDocument
            {
                ["chunkId"] = c.ChunkId(document),
                ["tenantId"] = document.TenantId,
                ["documentId"] = document.DocumentId,
                ["sourceType"] = document.SourceType,
                ["title"] = c.Chunk.Breadcrumb,
                ["content"] = c.Chunk.Content,
                ["contentVector"] = c.Vector.ToArray(),
                ["allowedPrincipals"] = document.AllowedPrincipals,
                ["sourceUrl"] = document.SourceUrl,
                ["updatedAt"] = updatedAt,
            });

            await _searchClient.IndexDocumentsAsync(
                IndexDocumentsBatch.MergeOrUpload(searchDocuments), cancellationToken: cancellationToken);

            foreach (var chunk in chunks)
            {
                var record = new ChunkRecord(
                    Id: chunk.ChunkId(document),
                    TenantId: document.TenantId,
                    DocumentId: document.DocumentId,
                    ChunkIndex: chunk.Chunk.Index,
                    Breadcrumb: chunk.Chunk.Breadcrumb,
                    Content: chunk.Chunk.Content,
                    TokenCount: chunk.Chunk.TokenCount,
                    ContentHash: contentHash,
                    UpdatedAt: updatedAt);

                await _chunks.UpsertItemAsync(record, new PartitionKey(document.TenantId),
                    cancellationToken: cancellationToken);
            }
        }

        await DeleteStaleChunksAsync(document, chunks.Count, cancellationToken);

        LogIndexed(logger, chunks.Count, document.DocumentId, document.TenantId);
    }

    /// <summary>Remove chunks beyond the new count — leftovers from a longer previous version.</summary>
    private async Task DeleteStaleChunksAsync(
        DocumentChanged document, int newCount, CancellationToken cancellationToken)
    {
        var options = new SearchOptions
        {
            Filter = $"tenantId eq '{document.TenantId}' and documentId eq '{document.DocumentId}'",
            Size = 1000,
        };
        options.Select.Add("chunkId");

        var response = await _searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken);
        var stale = new List<string>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            var chunkId = (string)result.Document["chunkId"];
            var marker = chunkId.LastIndexOf('-');
            if (marker >= 0 && int.TryParse(chunkId[(marker + 1)..], out var index) && index >= newCount)
            {
                stale.Add(chunkId);
            }
        }

        if (stale.Count == 0)
        {
            return;
        }

        await _searchClient.DeleteDocumentsAsync("chunkId", stale, cancellationToken: cancellationToken);
        foreach (var chunkId in stale)
        {
            await _chunks.DeleteItemAsync<ChunkRecord>(chunkId, new PartitionKey(document.TenantId),
                cancellationToken: cancellationToken);
        }

        LogStaleDeleted(logger, stale.Count, document.DocumentId);
    }

    /// <summary>Index schemas are code, not infra: create/update the chunks index on first use.</summary>
    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexEnsured)
        {
            return;
        }

        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_indexEnsured)
            {
                return;
            }

            var index = new SearchIndex(IndexName)
            {
                Fields =
                {
                    new SimpleField("chunkId", SearchFieldDataType.String) { IsKey = true },
                    new SimpleField("tenantId", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("sourceType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                    new SearchableField("title"),
                    new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                    new VectorSearchField("contentVector", VectorDimensions, "hnsw-default"),
                    new SimpleField("allowedPrincipals", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                    new SimpleField("sourceUrl", SearchFieldDataType.String),
                    new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                },
                VectorSearch = new VectorSearch
                {
                    Profiles = { new VectorSearchProfile("hnsw-default", "hnsw-algorithm") },
                    Algorithms = { new HnswAlgorithmConfiguration("hnsw-algorithm") },
                },
            };

            await searchIndexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);
            _indexEnsured = true;
            LogIndexEnsured(logger, IndexName);
        }
        finally
        {
            _indexGate.Release();
        }
    }

    public async Task<string?> GetContentHashAsync(string tenantId, string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _chunks.ReadItemAsync<ChunkRecord>(
                $"{tenantId}-{documentId}-0", new PartitionKey(tenantId), cancellationToken: cancellationToken);
            return response.Resource.ContentHash;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose() => _indexGate.Dispose();

    /// <summary>Cosmos chunk record; serialized camelCase (id is the chunk id).</summary>
    private sealed record ChunkRecord(
        string Id,
        string TenantId,
        string DocumentId,
        int ChunkIndex,
        string Breadcrumb,
        string Content,
        int TokenCount,
        string ContentHash,
        DateTimeOffset UpdatedAt);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Indexed {ChunkCount} chunks for document {DocumentId} (tenant {TenantId}) into AI Search + Cosmos")]
    private static partial void LogIndexed(ILogger logger, int chunkCount, string documentId, string tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Deleted {StaleCount} stale chunks for document {DocumentId}")]
    private static partial void LogStaleDeleted(ILogger logger, int staleCount, string documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search index '{IndexName}' ensured")]
    private static partial void LogIndexEnsured(ILogger logger, string indexName);
}