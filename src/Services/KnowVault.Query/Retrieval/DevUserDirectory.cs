using KnowVault.Domain.Security;

namespace KnowVault.Query.Retrieval;

/// <summary>
/// Local stand-in for Entra ID group membership: users and their groups come
/// from configuration ("DevUsers": { "alice": ["hr"] }). Resolved server-side
/// so the client can present a user name but can never choose its own groups —
/// the same trust shape as the Graph transitive-group expansion (with Redis
/// cache) that replaces this when real Entra ID sign-in lands.
/// </summary>
public sealed class DevUserDirectory(IConfiguration configuration)
{
    public PrincipalContext Resolve(string tenantId, string userId)
    {
        var groups = configuration.GetSection($"DevUsers:{userId}").Get<string[]>() ?? [];
        return new PrincipalContext(tenantId, userId, groups);
    }
}