using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

using KnowVault.Contracts.Retrieval;

using OpenAI.Embeddings;

namespace KnowVault.Query.Retrieval;

/// <summary>
/// Hybrid retrieval: BM25 keyword scoring and vector similarity in one
/// request, fused by Reciprocal Rank Fusion inside AI Search. The semantic
/// reranker joins when the service moves off the free tier (Phase 2 polish).
/// The tenant filter is mandatory on every query; Phase 3 replaces it with
/// the full SecurityTrimmingService (tenant + allowedPrincipals from JWT).
/// </summary>
public sealed partial class HybridRetriever(
    SearchClient searchClient,
    AzureOpenAIClient openAiClient,
    IConfiguration configuration,
    ILogger<HybridRetriever> logger)
{
    private const int VectorCandidates = 50;

    private readonly EmbeddingClient _embeddings =
        openAiClient.GetEmbeddingClient(configuration["Azure:OpenAI:EmbeddingDeployment"] ?? "text-embedding-3-large");

    public async Task<QueryResponse> RetrieveAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var embedding = await _embeddings.GenerateEmbeddingAsync(request.Question, cancellationToken: cancellationToken);

        var options = new SearchOptions
        {
            // Never from user input beyond the (validated) tenant id.
            Filter = $"tenantId eq '{request.TenantId}'",
            Size = Math.Clamp(request.Top, 1, 20),
            VectorSearch = new()
            {
                Queries =
                {
                    new VectorizedQuery(embedding.Value.ToFloats())
                    {
                        KNearestNeighborsCount = VectorCandidates,
                        Fields = { "contentVector" },
                    },
                },
            },
        };
        foreach (var field in (string[])["chunkId", "documentId", "title", "content", "sourceUrl"])
        {
            options.Select.Add(field);
        }

        var response = await searchClient.SearchAsync<SearchDocument>(request.Question, options, cancellationToken);

        var chunks = new List<RetrievedChunk>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            chunks.Add(new RetrievedChunk(
                ChunkId: (string)result.Document["chunkId"],
                DocumentId: (string)result.Document["documentId"],
                Title: (string)result.Document["title"],
                Content: (string)result.Document["content"],
                SourceUrl: result.Document["sourceUrl"] as string,
                Score: result.Score ?? 0));
        }

        LogRetrieved(logger, chunks.Count, request.TenantId);
        return new QueryResponse(chunks);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid retrieval returned {ChunkCount} chunks (tenant {TenantId})")]
    private static partial void LogRetrieved(ILogger logger, int chunkCount, string tenantId);
}