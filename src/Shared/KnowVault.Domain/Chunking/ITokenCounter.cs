namespace KnowVault.Domain.Chunking;

/// <summary>
/// Counts tokens the way the embedding model will. Abstracted so unit tests
/// can use a deterministic counter and chunk-size budgets stay model-accurate.
/// </summary>
public interface ITokenCounter
{
    int Count(string text);
}