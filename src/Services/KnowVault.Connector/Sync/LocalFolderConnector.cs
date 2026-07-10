using System.Security.Cryptography;

using KnowVault.Domain.Security;

namespace KnowVault.Connector.Sync;

/// <summary>
/// Reference connector: syncs a local inbox folder laid out as
/// {inbox}/{tenantId}/{file}. An optional sidecar "{file}.acl" (one principal
/// per line: user:x / group:y) restricts access; without one the document is
/// tenant-wide. Drop a file in → it becomes searchable; edit it → answers
/// update next sync; delete it → its chunks are tombstoned away.
/// </summary>
public sealed class LocalFolderConnector(string inboxRoot) : ISourceConnector
{
    private const string AclSuffix = ".acl";

    public string SourceId => "local-folder";

    public Task<IReadOnlyList<SourceItem>> ListAsync(CancellationToken cancellationToken)
    {
        var items = new List<SourceItem>();
        if (!Directory.Exists(inboxRoot))
        {
            return Task.FromResult<IReadOnlyList<SourceItem>>(items);
        }

        foreach (var tenantDir in Directory.EnumerateDirectories(inboxRoot))
        {
            var tenantId = Path.GetFileName(tenantDir);
            if (!SecurityTrimming.IsValidSegment(tenantId))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(tenantDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.EndsWith(AclSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
                items.Add(new SourceItem(
                    TenantId: tenantId,
                    ExternalId: $"{tenantId}/{Path.GetFileName(file)}",
                    FileName: Path.GetFileName(file),
                    ContentHash: hash,
                    AllowedPrincipals: ReadAcl(file, tenantId),
                    SourceUrl: null));
            }
        }

        return Task.FromResult<IReadOnlyList<SourceItem>>(items);
    }

    public async Task<BinaryData> FetchAsync(string externalId, CancellationToken cancellationToken)
    {
        var parts = externalId.Split('/', 2);
        var path = Path.Combine(inboxRoot, parts[0], parts[1]);
        return BinaryData.FromBytes(await File.ReadAllBytesAsync(path, cancellationToken));
    }

    private static List<string> ReadAcl(string file, string tenantId)
    {
        var aclFile = file + AclSuffix;
        if (!File.Exists(aclFile))
        {
            return [$"tenant:{tenantId}:all"];
        }

        var principals = File.ReadAllLines(aclFile)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("user:", StringComparison.Ordinal) ||
                        l.StartsWith("group:", StringComparison.Ordinal))
            .ToList();
        return principals.Count > 0 ? principals : [$"tenant:{tenantId}:all"];
    }
}