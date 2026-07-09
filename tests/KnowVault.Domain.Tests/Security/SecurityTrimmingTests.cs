using KnowVault.Domain.Security;

namespace KnowVault.Domain.Tests.Security;

public class SecurityTrimmingTests
{
    [Fact]
    public void Filter_contains_tenant_user_groups_and_sentinel()
    {
        var principal = new PrincipalContext("acme", "alice", ["hr", "leadership"]);

        var filter = SecurityTrimming.BuildFilter(principal);

        Assert.Equal(
            "tenantId eq 'acme' and allowedPrincipals/any(p: search.in(p, 'user:alice|group:hr|group:leadership|tenant:acme:all', '|'))",
            filter);
    }

    [Fact]
    public void User_with_no_groups_still_gets_tenant_wide_documents()
    {
        var filter = SecurityTrimming.BuildFilter(new PrincipalContext("acme", "mallory", []));

        Assert.Contains("user:mallory", filter, StringComparison.Ordinal);
        Assert.Contains("tenant:acme:all", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("group:", filter, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("acme' or tenantId ne '")] // OData injection attempt
    [InlineData("a|b")]                    // separator collision
    [InlineData("")]
    [InlineData("has space")]
    public void Hostile_tenant_ids_are_rejected(string tenantId)
    {
        var principal = new PrincipalContext(tenantId, "alice", []);

        Assert.Throws<ArgumentException>(() => SecurityTrimming.BuildFilter(principal));
    }

    [Theory]
    [InlineData("alice' or 1 eq 1")]
    [InlineData("a|b")]
    public void Hostile_user_ids_are_rejected(string userId)
    {
        var principal = new PrincipalContext("acme", userId, []);

        Assert.Throws<ArgumentException>(() => SecurityTrimming.BuildFilter(principal));
    }

    [Fact]
    public void Hostile_group_names_are_rejected()
    {
        var principal = new PrincipalContext("acme", "alice", ["ok-group", "bad' group"]);

        Assert.Throws<ArgumentException>(() => SecurityTrimming.BuildFilter(principal));
    }

    [Fact]
    public void Entra_object_ids_are_valid_segments()
    {
        Assert.True(SecurityTrimming.IsValidSegment(Guid.NewGuid().ToString()));
        Assert.True(SecurityTrimming.IsValidSegment("user.name@contoso.com"));
    }
}