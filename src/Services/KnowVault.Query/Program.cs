using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;

using KnowVault.Contracts.Retrieval;
using KnowVault.Domain.Security;
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
    builder.Services.AddSingleton<DevUserDirectory>();
    builder.Services.AddSingleton<HybridRetriever>();
}

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "KnowVault.Query");

app.MapPost("/api/query", async Task<Results<Ok<QueryResponse>, BadRequest<string>>> (
    QueryRequest request,
    HttpContext http,
    DevUserDirectory users,
    HybridRetriever retriever,
    CancellationToken cancellationToken) =>
{
    // Identity comes from headers (dev) / JWT claims (production) — never the body.
    var tenantId = http.Request.Headers[IdentityHeaders.Tenant].ToString();
    var userId = http.Request.Headers[IdentityHeaders.User].ToString();
    if (userId.Length == 0)
    {
        userId = "anonymous";
    }

    if (!SecurityTrimming.IsValidSegment(tenantId) || !SecurityTrimming.IsValidSegment(userId) ||
        string.IsNullOrWhiteSpace(request.Question))
    {
        return TypedResults.BadRequest("A valid identity and a non-empty question are required.");
    }

    var principal = users.Resolve(tenantId, userId);
    return TypedResults.Ok(await retriever.RetrieveAsync(principal, request, cancellationToken));
});

app.Run();