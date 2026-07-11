using System.Security.Claims;

using KnowVault.Domain.Security;

namespace KnowVault.Query.Retrieval;

/// <summary>
/// Builds the PrincipalContext from a validated Entra ID token. Object ids
/// map to friendly names via configuration so existing index ACLs
/// (user:alice / group:hr) keep working; unmapped ids pass through raw —
/// which is exactly the production behavior described in ADR-002, where
/// ACLs are written as Entra object ids in the first place.
/// </summary>
public sealed class EntraIdentity(IConfiguration configuration)
{
    /// <summary>Single-organization demo: every signed-in user belongs to one app tenant.</summary>
    public string AppTenant { get; } = configuration["Entra:AppTenant"] ?? "eval";

    public PrincipalContext Resolve(ClaimsPrincipal user)
    {
        var oid = user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? user.FindFirstValue("oid")
            ?? throw new InvalidOperationException("Token has no oid claim.");

        var userId = configuration[$"Entra:UserNames:{oid}"] ?? oid;
        var groups = user.FindAll("groups")
            .Select(c => configuration[$"Entra:GroupNames:{c.Value}"] ?? c.Value)
            .ToList();

        return new PrincipalContext(AppTenant, userId, groups);
    }
}