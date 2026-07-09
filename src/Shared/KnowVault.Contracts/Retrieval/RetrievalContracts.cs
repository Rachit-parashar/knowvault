namespace KnowVault.Contracts.Retrieval;

/// <summary>
/// Query/Answer service contracts. TenantId travels in the request body only
/// until Phase 3, when it moves to validated JWT claims and these DTOs lose
/// the field entirely — the caller must never be able to choose a tenant.
/// </summary>
public sealed record QueryRequest(string TenantId, string Question, int Top = 8);

public sealed record QueryResponse(IReadOnlyList<RetrievedChunk> Chunks);

/// <summary>One retrieval hit, ready for prompt assembly and citation display.</summary>
public sealed record RetrievedChunk(
    string ChunkId,
    string DocumentId,
    string Title,
    string Content,
    string? SourceUrl,
    double Score);

public sealed record AskRequest(string TenantId, string Question);

/// <summary>First SSE event of an answer stream: the numbered sources [1]..[n].</summary>
public sealed record AnswerSource(int Marker, string ChunkId, string DocumentId, string Title, string? SourceUrl);