using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace KnowVault.Connector.Sync;

/// <summary>
/// Google Drive source: a service account (shared into the target folder as
/// Viewer) lists the folder tree recursively, native Docs export as markdown,
/// Sheets as CSV, and supported binaries download directly. Change detection
/// uses Drive's md5/version, so unchanged files never re-ingest. The sync
/// engine on top handles edits and deletion tombstones like any other source.
/// </summary>
public sealed partial class GoogleDriveConnector : ISourceConnector, IDisposable
{
    private readonly DriveService _drive;
    private readonly string _tenantId;
    private readonly string _rootFolderId;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleDriveConnector> _logger;

    public GoogleDriveConnector(
        string tenantId,
        string rootFolderId,
        string serviceAccountJson,
        IConfiguration configuration,
        ILogger<GoogleDriveConnector> logger)
    {
        _tenantId = tenantId;
        _rootFolderId = rootFolderId;
        _configuration = configuration;
        _logger = logger;
        // FromJson is marked obsolete in favor of CredentialFactory, but remains
        // the documented path for service-account JSON keys; the flagged risk
        // (credential type confusion) doesn't apply to a fixed key we own.
#pragma warning disable CS0618
        var credential = GoogleCredential.FromJson(serviceAccountJson);
#pragma warning restore CS0618
        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential.CreateScoped(DriveService.Scope.DriveReadonly),
            ApplicationName = "KnowVault",
        });
    }

    public string SourceId => "google-drive";

    public async Task<IReadOnlyList<SourceItem>> ListAsync(CancellationToken cancellationToken)
    {
        var items = new List<SourceItem>();
        var folders = new Queue<string>();
        folders.Enqueue(_rootFolderId);

        while (folders.Count > 0)
        {
            var folderId = folders.Dequeue();
            string? pageToken = null;
            do
            {
                var request = _drive.Files.List();
                request.Q = $"'{folderId}' in parents and trashed = false";
                request.Fields = "nextPageToken, files(id, name, mimeType, md5Checksum, version, webViewLink, permissions(type, emailAddress))";
                request.PageSize = 100;
                request.PageToken = pageToken;
                request.SupportsAllDrives = true;
                request.IncludeItemsFromAllDrives = true;

                var page = await request.ExecuteAsync(cancellationToken);
                foreach (var file in page.Files)
                {
                    if (file.MimeType == GoogleDriveMapping.FolderMimeType)
                    {
                        folders.Enqueue(file.Id);
                        continue;
                    }

                    var mapping = GoogleDriveMapping.TryMapFile(file.Name, file.MimeType, file.Md5Checksum, file.Version);
                    if (mapping is null)
                    {
                        LogSkipped(_logger, file.Name, file.MimeType);
                        continue;
                    }

                    var principals = GoogleDriveMapping.MapPermissions(
                        (file.Permissions ?? []).Select(p => ((string?)p.Type, (string?)p.EmailAddress)),
                        _tenantId,
                        email => _configuration[$"Connector:GoogleDrive:UserNames:{email}"]);

                    items.Add(new SourceItem(
                        TenantId: _tenantId,
                        ExternalId: $"{_tenantId}/{file.Id}",
                        FileName: mapping.FileName,
                        ContentHash: mapping.ContentHash,
                        AllowedPrincipals: principals,
                        SourceUrl: file.WebViewLink));
                }

                pageToken = page.NextPageToken;
            }
            while (pageToken is not null);
        }

        LogListed(_logger, items.Count, _rootFolderId);
        return items;
    }

    public async Task<BinaryData> FetchAsync(string externalId, CancellationToken cancellationToken)
    {
        var fileId = externalId.Split('/', 2)[1];

        var metadataRequest = _drive.Files.Get(fileId);
        metadataRequest.Fields = "mimeType, name";
        metadataRequest.SupportsAllDrives = true;
        var metadata = await metadataRequest.ExecuteAsync(cancellationToken);

        var mapping = GoogleDriveMapping.TryMapFile(metadata.Name, metadata.MimeType, "x", 0)
            ?? throw new InvalidOperationException($"Drive file '{metadata.Name}' is no longer a supported type.");

        using var stream = new MemoryStream();
        if (mapping.ExportMimeType is not null)
        {
            await _drive.Files.Export(fileId, mapping.ExportMimeType).DownloadAsync(stream, cancellationToken);
        }
        else
        {
            var download = _drive.Files.Get(fileId);
            download.SupportsAllDrives = true;
            await download.DownloadAsync(stream, cancellationToken);
        }

        return BinaryData.FromBytes(stream.ToArray());
    }

    public void Dispose() => _drive.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Drive listing: {ItemCount} supported file(s) under folder {FolderId}")]
    private static partial void LogListed(ILogger logger, int itemCount, string folderId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping unsupported Drive item {Name} ({MimeType})")]
    private static partial void LogSkipped(ILogger logger, string name, string mimeType);
}