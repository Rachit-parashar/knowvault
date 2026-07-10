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

builder.Services.AddHostedService<SyncEngine>();

var host = builder.Build();
host.Run();