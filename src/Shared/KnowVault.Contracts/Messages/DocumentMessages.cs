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

/// <summary>Emitted when a document is created or its content changes (Connector sync or direct upload).</summary>
/// <param name="BlobPath">Where the raw content sits in the uploads container; set for direct-upload documents.</param>
/// <param name="ContentHash">SHA-256 of the content when the producer already knows it; Ingestion computes it otherwise.</param>
public sealed record DocumentChanged(
    string TenantId,
    string SourceId,
    string DocumentId,
    string SourceType,
    string? BlobPath,
    string? SourceUrl,
    string? ContentHash,
    IReadOnlyList<string> AllowedPrincipals,
    DateTimeOffset DetectedAt);

/// <summary>Emitted by Connector when a document is removed or access is revoked entirely (tombstone).</summary>
public sealed record DocumentDeleted(
    string TenantId,
    string SourceId,
    string DocumentId,
    DateTimeOffset DetectedAt);