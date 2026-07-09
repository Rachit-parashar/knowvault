namespace KnowVault.Domain.Security;

/// <summary>
/// The single place the mandatory retrieval filter is constructed — the heart
/// of the platform's security model. Every search call includes this filter,
/// so content the caller may not read is never a candidate: it cannot leak
/// through ranking, context assembly, or logging.
/// </summary>
public static class SecurityTrimming
{
    /// <summary>
    /// Builds the OData filter enforcing tenant isolation and ACL trimming:
    /// <c>tenantId eq '{tid}' and allowedPrincipals/any(p: search.in(p, 'user:u|group:g|tenant:t:all', '|'))</c>.
    /// </summary>
    public static string BuildFilter(PrincipalContext principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ValidateSegment(principal.TenantId, nameof(principal.TenantId));
        ValidateSegment(principal.UserId, nameof(principal.UserId));
        foreach (var group in principal.Groups)
        {
            ValidateSegment(group, "group");
        }

        var principals = string.Join('|', principal.ToPrincipals());
        return $"tenantId eq '{principal.TenantId}' and allowedPrincipals/any(p: search.in(p, '{principals}', '|'))";
    }

    /// <summary>
    /// Identity segments are restricted to a safe alphabet so they can never
    /// break out of the filter expression or collide with the '|' separator.
    /// Entra object ids (GUIDs) always satisfy this.
    /// </summary>
    public static bool IsValidSegment(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 64 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '@');

    private static void ValidateSegment(string value, string name)
    {
        if (!IsValidSegment(value))
        {
            throw new ArgumentException($"'{value}' is not a valid identity segment.", name);
        }
    }
}