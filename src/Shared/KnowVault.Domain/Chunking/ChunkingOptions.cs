namespace KnowVault.Domain.Chunking;

/// <summary>
/// Per-tenant chunking configuration. Defaults follow the build plan:
/// structure-aware splitting first, then recursive token-based splitting
/// to ~512 tokens with 15% overlap.
/// </summary>
public sealed record ChunkingOptions
{
    public const int MinTokensPerChunk = 64;
    public const int MaxTokensPerChunk = 2048;

    public int TargetTokensPerChunk { get; init; } = 512;

    /// <summary>Overlap between consecutive chunks as a fraction of chunk size.</summary>
    public double OverlapRatio { get; init; } = 0.15;

    /// <summary>Prepend "Title &gt; Section" breadcrumbs to each chunk's embedded text.</summary>
    public bool IncludeBreadcrumbs { get; init; } = true;

    /// <summary>Keep extracted tables whole (as markdown) where they fit in a chunk.</summary>
    public bool KeepTablesWhole { get; init; } = true;

    public int OverlapTokens => (int)(TargetTokensPerChunk * OverlapRatio);

    public void Validate()
    {
        if (TargetTokensPerChunk is < MinTokensPerChunk or > MaxTokensPerChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetTokensPerChunk),
                $"Must be between {MinTokensPerChunk} and {MaxTokensPerChunk}.");
        }

        if (OverlapRatio is < 0 or >= 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(OverlapRatio),
                "Must be in [0, 0.5) — an overlap of half or more never terminates.");
        }
    }
}