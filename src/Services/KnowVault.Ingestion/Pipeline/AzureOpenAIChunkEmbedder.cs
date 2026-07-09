using Azure.AI.OpenAI;

using OpenAI.Embeddings;

namespace KnowVault.Ingestion.Pipeline;

/// <summary>
/// Real embeddings via Azure OpenAI (text-embedding-3-large, 3072 dimensions).
/// Batches arrive pre-sized from the pipeline; one API call per batch.
/// </summary>
public sealed class AzureOpenAIChunkEmbedder(AzureOpenAIClient client, IConfiguration configuration) : IChunkEmbedder
{
    private readonly EmbeddingClient _embeddings =
        client.GetEmbeddingClient(configuration["Azure:OpenAI:EmbeddingDeployment"] ?? "text-embedding-3-large");

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var response = await _embeddings.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);
        return [.. response.Value.Select(e => e.ToFloats())];
    }
}