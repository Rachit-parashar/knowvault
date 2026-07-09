using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

using KnowVault.Contracts.Retrieval;
using KnowVault.Domain.Security;

using OpenAI.Embeddings;

namespace KnowVault.Query.Retrieval;

/// <summary>
/// Hybrid retrieval: BM25 keyword scoring and vector similarity in one
/// request, fused by Reciprocal Rank Fusion inside AI Search. The semantic
/// reranker joins when the service moves off the free tier (Phase 2 polish).
/// Every query carries the mandatory SecurityTrimming filter built from the
/// caller's PrincipalContext — there is no code path to search without it.
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

    public async Task<QueryResponse> RetrieveAsync(
        PrincipalContext principal, QueryRequest request, CancellationToken cancellationToken)
    {
        var embedding = await _embeddings.GenerateEmbeddingAsync(request.Question, cancellationToken: cancellationToken);

        var options = new SearchOptions
        {
            // The one and only place retrieval filters are built (ADR-002).
            Filter = SecurityTrimming.BuildFilter(principal),
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

        LogRetrieved(logger, chunks.Count, principal.TenantId, principal.UserId);
        return new QueryResponse(chunks);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hybrid retrieval returned {ChunkCount} chunks (tenant {TenantId}, user {UserId})")]
    private static partial void LogRetrieved(ILogger logger, int chunkCount, string tenantId, string userId);
}