using KnowVault.Domain.Security;

namespace KnowVault.Connector.Sync;

/// <summary>How a Drive file enters the pipeline: its staged name, change hash, and export format (null = direct download).</summary>
public sealed record DriveFileMapping(string FileName, string ContentHash, string? ExportMimeType);

/// <summary>
/// Pure mapping rules for Google Drive items — separated from the API client
/// so they are unit-testable. Native Google Docs export as markdown (heading
/// structure survives into the chunker), Sheets as CSV text; binary files
/// pass through when the pipeline supports their extension.
/// </summary>
public static class GoogleDriveMapping
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";
    public const string DocMimeType = "application/vnd.google-apps.document";
    public const string SheetMimeType = "application/vnd.google-apps.spreadsheet";

    private static readonly string[] SupportedBinaryExtensions = [".md", ".markdown", ".txt", ".pdf"];

    public static DriveFileMapping? TryMapFile(string name, string mimeType, string? md5Checksum, long? version)
    {
        // Something must change when content changes; Drive gives md5 for
        // binary files and a monotonically increasing version for native docs.
        var hash = md5Checksum ?? (version.HasValue ? $"v{version.Value}" : null);
        if (hash is null)
        {
            return null;
        }

        return mimeType switch
        {
            DocMimeType => new DriveFileMapping($"{SanitizeFileName(name)}.md", hash, "text/markdown"),
            SheetMimeType => new DriveFileMapping($"{SanitizeFileName(name)}.txt", hash, "text/csv"),
            _ when SupportedBinaryExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()) =>
                new DriveFileMapping(
                    SanitizeFileName(Path.GetFileNameWithoutExtension(name)) + Path.GetExtension(name).ToLowerInvariant(),
                    hash, null),
            _ => null, // unsupported type — skipped, logged by the connector
        };
    }

    /// <summary>Blob-path-safe file name: letters, digits, dash, underscore, dot.</summary>
    public static string SanitizeFileName(string name)
    {
        var cleaned = new string([.. name.Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')]).Trim('-', '.');
        return cleaned.Length > 0 ? cleaned : "untitled";
    }

    /// <summary>
    /// Drive permissions → principal strings. Emails resolve through the
    /// supplied map (mirroring the Entra name maps) or pass through raw when
    /// they form valid segments. Domain-wide or public sharing — and an
    /// unreadable ACL — degrade to tenant-wide visibility, the same default
    /// as direct uploads.
    /// </summary>
    public static IReadOnlyList<string> MapPermissions(
        IEnumerable<(string? Type, string? Email)> permissions,
        string tenantId,
        Func<string, string?> resolveName)
    {
        var principals = new List<string>();

        foreach (var (type, email) in permissions)
        {
            switch (type)
            {
                case "anyone" or "domain":
                    return [$"tenant:{tenantId}:all"];
                case "user" or "group" when !string.IsNullOrEmpty(email):
                    var name = resolveName(email) ?? email;
                    if (SecurityTrimming.IsValidSegment(name))
                    {
                        principals.Add($"{(type == "user" ? "user" : "group")}:{name}");
                    }

                    break;
            }
        }

        return principals.Count > 0 ? principals : [$"tenant:{tenantId}:all"];
    }
}