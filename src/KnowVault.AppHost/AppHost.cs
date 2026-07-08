var builder = DistributedApplication.CreateBuilder(args);

// Core request path
var query = builder.AddProject<Projects.KnowVault_Query>("query");
var answer = builder.AddProject<Projects.KnowVault_Answer>("answer")
    .WithReference(query);

var admin = builder.AddProject<Projects.KnowVault_Admin>("admin");

var gateway = builder.AddProject<Projects.KnowVault_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(query)
    .WithReference(answer)
    .WithReference(admin);

// Ingestion path (Service Bus workers — emulator wiring lands in Phase 1)
builder.AddProject<Projects.KnowVault_Connector>("connector");
builder.AddProject<Projects.KnowVault_Ingestion>("ingestion");

// Eval harness
builder.AddProject<Projects.KnowVault_Eval>("eval")
    .WithReference(query)
    .WithReference(answer);

builder.Build().Run();