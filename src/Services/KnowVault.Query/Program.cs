using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;

using KnowVault.Contracts.Retrieval;
using KnowVault.Query.Retrieval;

using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var searchEndpoint = builder.Configuration["Azure:Search:Endpoint"];
var openAiEndpoint = builder.Configuration["Azure:OpenAI:Endpoint"];

if (!string.IsNullOrEmpty(searchEndpoint) && !string.IsNullOrEmpty(openAiEndpoint))
{
    var credential = new DefaultAzureCredential();
    builder.Services.AddSingleton(new SearchClient(new Uri(searchEndpoint), "chunks", credential));
    builder.Services.AddSingleton(new AzureOpenAIClient(new Uri(openAiEndpoint), credential));
    builder.Services.AddSingleton<HybridRetriever>();
}

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "KnowVault.Query");

app.MapPost("/api/query", async Task<Results<Ok<QueryResponse>, BadRequest<string>>> (
    QueryRequest request,
    HybridRetriever retriever,
    CancellationToken cancellationToken) =>
{
    if (!IsSafeSegment(request.TenantId) || string.IsNullOrWhiteSpace(request.Question))
    {
        return TypedResults.BadRequest("A valid tenantId and a non-empty question are required.");
    }

    return TypedResults.Ok(await retriever.RetrieveAsync(request, cancellationToken));
});

app.Run();

static bool IsSafeSegment(string value) =>
    !string.IsNullOrEmpty(value) && value.Length <= 64 &&
    value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');