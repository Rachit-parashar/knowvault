var builder = DistributedApplication.CreateBuilder(args);

// Storage: Azurite locally, real Storage account in Azure.
var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var uploads = storage.AddBlobContainer("uploads");
var syncState = storage.AddBlobContainer("sync-state");

// Messaging: Service Bus emulator locally, real namespace in Azure.
var messaging = builder.AddAzureServiceBus("messaging").RunAsEmulator();
var documentChanged = messaging.AddServiceBusQueue("document-changed");
documentChanged.Resource.MaxDeliveryCount = 5;
var documentDeleted = messaging.AddServiceBusQueue("document-deleted");
documentDeleted.Resource.MaxDeliveryCount = 5;

// Core request path
var query = builder.AddProject<Projects.KnowVault_Query>("query");
var answer = builder.AddProject<Projects.KnowVault_Answer>("answer")
    .WithReference(query);

var admin = builder.AddProject<Projects.KnowVault_Admin>("admin")
    .WithReference(uploads)
    .WithReference(messaging)
    .WaitFor(uploads)
    .WaitFor(messaging);

// Ingestion path
builder.AddProject<Projects.KnowVault_Connector>("connector")
    .WithReference(uploads)
    .WithReference(syncState)
    .WithReference(messaging)
    .WithEnvironment("Connector__InboxPath", Path.Combine(builder.AppHostDirectory, "..", "..", "connector-inbox"))
    .WithEnvironment("Sync__IntervalSeconds", "15")
    .WaitFor(uploads)
    .WaitFor(messaging);
builder.AddProject<Projects.KnowVault_Ingestion>("ingestion")
    .WithReference(uploads)
    .WithReference(messaging)
    .WaitFor(uploads)
    .WaitFor(messaging);

// Eval harness
builder.AddProject<Projects.KnowVault_Eval>("eval")
    .WithReference(admin)
    .WithReference(query)
    .WithReference(answer)
    .WithEnvironment("EVALS_DIR", Path.Combine(builder.AppHostDirectory, "..", "..", "evals"));

builder.Build().Run();