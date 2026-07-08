namespace KnowVault.Ingestion.Pipeline;

/// <summary>
/// Batched embedding generation. The Azure OpenAI implementation
/// (text-embedding-3-large) arrives with the Phase 1 infra deploy; locally
/// without configuration the null implementation keeps the pipeline runnable.
/// </summary>
public interface IChunkEmbedder
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>Placeholder embedder: empty vectors, so indexing stays exercisable end-to-end.</summary>
public sealed class NullChunkEmbedder : IChunkEmbedder
{
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
            [.. texts.Select(_ => ReadOnlyMemory<float>.Empty)]);
}