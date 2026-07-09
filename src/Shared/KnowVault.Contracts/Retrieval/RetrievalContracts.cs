namespace KnowVault.Contracts.Retrieval;

/// <summary>
/// Query/Answer service contracts. Identity (tenant, user) deliberately does
/// NOT appear in these bodies: it travels as caller identity — dev headers
/// locally (X-Dev-Tenant / X-Dev-User), validated JWT claims in production —
/// so a caller can never choose who they are.
/// </summary>
public sealed record QueryRequest(string Question, int Top = 8);

public sealed record QueryResponse(IReadOnlyList<RetrievedChunk> Chunks);

/// <summary>One retrieval hit, ready for prompt assembly and citation display.</summary>
public sealed record RetrievedChunk(
    string ChunkId,
    string DocumentId,
    string Title,
    string Content,
    string? SourceUrl,
    double Score);

public sealed record AskRequest(string Question);

/// <summary>Dev identity header names, replaced by JWT claims when Entra ID lands.</summary>
public static class IdentityHeaders
{
    public const string Tenant = "X-Dev-Tenant";
    public const string User = "X-Dev-User";
}

/// <summary>First SSE event of an answer stream: the numbered sources [1]..[n].</summary>
public sealed record AnswerSource(int Marker, string ChunkId, string DocumentId, string Title, string? SourceUrl);