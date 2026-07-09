using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents.Indexes;

using KnowVault.Ingestion;
using KnowVault.Ingestion.Pipeline;

using Microsoft.Azure.Cosmos;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureBlobContainerClient("uploads");
builder.AddAzureServiceBusClient("messaging");

// Real Azure OpenAI + AI Search + Cosmos when endpoints are configured
// (appsettings.Development.json); the null/logging pair otherwise, so the
// pipeline stays runnable without any cloud resources.
var openAiEndpoint = builder.Configuration["Azure:OpenAI:Endpoint"];
var searchEndpoint = builder.Configuration["Azure:Search:Endpoint"];
var cosmosEndpoint = builder.Configuration["Azure:Cosmos:Endpoint"];

if (!string.IsNullOrEmpty(openAiEndpoint) && !string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(cosmosEndpoint))
{
    // Locally this resolves to the developer's `az login`; in Azure it becomes
    // the Container App's Managed Identity. No keys in either case.
    var credential = new DefaultAzureCredential();

    builder.Services.AddSingleton(new AzureOpenAIClient(new Uri(openAiEndpoint), credential));
    builder.Services.AddSingleton(new SearchIndexClient(new Uri(searchEndpoint), credential));
    builder.Services.AddSingleton(new CosmosClient(cosmosEndpoint, credential, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
    }));

    builder.Services.AddSingleton<IChunkEmbedder, AzureOpenAIChunkEmbedder>();
    builder.Services.AddSingleton<IChunkSink, AzureChunkSink>();
}
else
{
    builder.Services.AddSingleton<IChunkEmbedder, NullChunkEmbedder>();
    builder.Services.AddSingleton<IChunkSink, LoggingChunkSink>();
}

var docIntelligenceEndpoint = builder.Configuration["Azure:DocumentIntelligence:Endpoint"];
if (!string.IsNullOrEmpty(docIntelligenceEndpoint))
{
    builder.Services.AddSingleton(
        new DocumentIntelligenceClient(new Uri(docIntelligenceEndpoint), new DefaultAzureCredential()));
    builder.Services.AddSingleton<IPdfExtractor, DocumentIntelligencePdfExtractor>();
}
else
{
    builder.Services.AddSingleton<IPdfExtractor, UnavailablePdfExtractor>();
}

builder.Services.AddSingleton<IngestionPipeline>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();