namespace KnowVault.Connector.Sync;

/// <summary>One document as seen at the source, with its access list.</summary>
/// <param name="ExternalId">Stable identity at the source (path, Graph item id, ...).</param>
/// <param name="ContentHash">Hash of the current content — drives change detection.</param>
public sealed record SourceItem(
    string TenantId,
    string ExternalId,
    string FileName,
    string ContentHash,
    IReadOnlyList<string> AllowedPrincipals,
    string? SourceUrl);

/// <summary>
/// A document source the sync engine can enumerate and fetch from. The
/// SharePoint implementation (Microsoft Graph app-only + delta queries) plugs
/// in here once an M365 tenant is available; LocalFolderConnector proves the
/// full sync loop — additions, edits, deletions, ACL capture — without one.
/// </summary>
public interface ISourceConnector
{
    /// <summary>Identifies this source in sync-state storage and DocumentChanged.SourceId.</summary>
    string SourceId { get; }

    Task<IReadOnlyList<SourceItem>> ListAsync(CancellationToken cancellationToken);

    Task<BinaryData> FetchAsync(string externalId, CancellationToken cancellationToken);
}