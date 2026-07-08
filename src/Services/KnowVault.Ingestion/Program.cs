using KnowVault.Ingestion;
using KnowVault.Ingestion.Pipeline;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureBlobContainerClient("uploads");
builder.AddAzureServiceBusClient("messaging");

// Azure OpenAI + AI Search + Cosmos implementations replace these once the
// Phase 1 resources are deployed; the null/logging pair keeps the pipeline
// runnable locally end-to-end.
builder.Services.AddSingleton<IChunkEmbedder, NullChunkEmbedder>();
builder.Services.AddSingleton<IChunkSink, LoggingChunkSink>();
builder.Services.AddSingleton<IngestionPipeline>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();