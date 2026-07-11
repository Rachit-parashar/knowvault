using KnowVault.Connector.Sync;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
// Both containers keyed: mixing one keyed and one non-keyed BlobContainerClient
// registration resolves the non-keyed parameter to null (learned the hard way).
builder.AddKeyedAzureBlobContainerClient("uploads");
builder.AddKeyedAzureBlobContainerClient("sync-state");
builder.AddAzureServiceBusClient("messaging");

// Local folder connector when an inbox is configured; the SharePoint
// connector (Graph app-only + delta queries) registers here in the same way
// once an M365 tenant is available.
var inbox = builder.Configuration["Connector:InboxPath"];
if (!string.IsNullOrEmpty(inbox))
{
    builder.Services.AddSingleton<ISourceConnector>(new LocalFolderConnector(inbox));
}

// Google Drive connector: a service account shared into the folder as Viewer.
// Key from a file path (local dev, .secrets/ is gitignored) or inline JSON
// (cloud: Key Vault / container secret).
var driveFolderId = builder.Configuration["Connector:GoogleDrive:FolderId"];
var driveKeyPath = builder.Configuration["Connector:GoogleDrive:ServiceAccountKeyPath"];
var driveKeyJson = builder.Configuration["Connector:GoogleDrive:ServiceAccountJson"]
    ?? (!string.IsNullOrEmpty(driveKeyPath) && File.Exists(driveKeyPath) ? File.ReadAllText(driveKeyPath) : null);
if (!string.IsNullOrEmpty(driveFolderId) && !string.IsNullOrEmpty(driveKeyJson))
{
    builder.Services.AddSingleton<ISourceConnector>(sp => new GoogleDriveConnector(
        builder.Configuration["Connector:GoogleDrive:Tenant"] ?? "gdrive",
        driveFolderId,
        driveKeyJson,
        builder.Configuration,
        sp.GetRequiredService<ILogger<GoogleDriveConnector>>()));
}

builder.Services.AddHostedService<SyncEngine>();

var host = builder.Build();
host.Run();