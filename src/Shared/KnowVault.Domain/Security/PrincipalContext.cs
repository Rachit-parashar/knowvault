namespace KnowVault.Domain.Security;

/// <summary>
/// The caller's verified identity for retrieval purposes. In production this
/// is built exclusively from validated JWT claims (tid/oid/groups); in local
/// development it comes from dev headers resolved against a test-user
/// directory. It is NEVER built from request bodies — the caller must not be
/// able to choose who they are.
/// </summary>
public sealed record PrincipalContext(string TenantId, string UserId, IReadOnlyList<string> Groups)
{
    /// <summary>The principal strings that may appear in a chunk's allowedPrincipals field.</summary>
    public IReadOnlyList<string> ToPrincipals() =>
    [
        $"user:{UserId}",
        .. Groups.Select(g => $"group:{g}"),
        $"tenant:{TenantId}:all",
    ];
}