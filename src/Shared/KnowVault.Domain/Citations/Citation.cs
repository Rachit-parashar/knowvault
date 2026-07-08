namespace KnowVault.Domain.Citations;

/// <summary>
/// A resolved citation: maps an inline marker [n] in a generated answer
/// to the exact source chunk it draws from.
/// </summary>
public sealed record Citation(
    int Marker,
    string ChunkId,
    string DocumentId,
    string Title,
    string? SourceUrl);