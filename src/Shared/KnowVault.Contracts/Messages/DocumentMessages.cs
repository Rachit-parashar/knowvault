namespace KnowVault.Contracts.Messages;

/// <summary>
/// Service Bus message contracts are versioned explicitly (v1 suffix on the
/// contract name, carried in the message's application properties) so
/// consumers can evolve independently of producers.
/// </summary>
public static class MessageContracts
{
    public const string DocumentChangedV1 = "knowvault.document-changed.v1";
    public const string DocumentDeletedV1 = "knowvault.document-deleted.v1";
}

/// <summary>Emitted by Connector when a document is created or its content hash changes.</summary>
public sealed record DocumentChanged(
    string TenantId,
    string SourceId,
    string DocumentId,
    string ContentHash,
    string SourceType,
    string? SourceUrl,
    IReadOnlyList<string> AllowedPrincipals,
    DateTimeOffset DetectedAt);

/// <summary>Emitted by Connector when a document is removed or access is revoked entirely (tombstone).</summary>
public sealed record DocumentDeleted(
    string TenantId,
    string SourceId,
    string DocumentId,
    DateTimeOffset DetectedAt);