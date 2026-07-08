using Microsoft.ML.Tokenizers;

namespace KnowVault.Domain.Chunking;

/// <summary>
/// cl100k_base token counting — the encoding used by text-embedding-3-large.
/// The tokenizer is expensive to construct, so a single instance is shared.
/// </summary>
public sealed class Cl100kTokenCounter : ITokenCounter
{
    private static readonly Lazy<TiktokenTokenizer> Tokenizer =
        new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"), LazyThreadSafetyMode.ExecutionAndPublication);

    public int Count(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Tokenizer.Value.CountTokens(text);
}