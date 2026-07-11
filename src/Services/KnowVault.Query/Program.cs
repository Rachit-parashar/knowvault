using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;

using KnowVault.Contracts.Retrieval;
using KnowVault.Domain.Security;
using KnowVault.Query.Retrieval;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Cosmos;

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
    builder.Services.AddSingleton<EntraIdentity>();
    builder.Services.AddSingleton<HybridRetriever>();

    var cosmosEndpoint = builder.Configuration["Azure:Cosmos:Endpoint"];
    if (!string.IsNullOrEmpty(cosmosEndpoint))
    {
        builder.Services.AddSingleton(new CosmosClient(cosmosEndpoint, credential, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
        }));
        builder.Services.AddSingleton<NeighborExpander>();
    }
}

// Entra ID (JWT) validation when configured. AllowDevHeaders keeps the
// header-identity fallback for local dev and the CI eval gate; production
// posture sets it false, making bearer tokens mandatory.
var entraTenantId = builder.Configuration["Entra:TenantId"];
var entraClientId = builder.Configuration["Entra:ClientId"];
var entraEnabled = !string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraClientId);
var allowDevHeaders = !entraEnabled || builder.Configuration.GetValue("Entra:AllowDevHeaders", false);

if (entraEnabled)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";
            options.TokenValidationParameters.ValidAudiences = [entraClientId, $"api://{entraClientId}"];
        });
    builder.Services.AddAuthorization();
}

var app = builder.Build();

app.MapDefaultEndpoints();
if (entraEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/", () => "KnowVault.Query");

app.MapPost("/api/query", async Task<Results<Ok<QueryResponse>, BadRequest<string>, UnauthorizedHttpResult>> (
    QueryRequest request,
    HttpContext http,
    DevUserDirectory users,
    EntraIdentity entra,
    HybridRetriever retriever,
    CancellationToken cancellationToken) =>
{
    PrincipalContext principal;

    if (http.User.Identity?.IsAuthenticated == true)
    {
        // Verified Entra token — the only identity source in strict mode.
        principal = entra.Resolve(http.User);
    }
    else if (allowDevHeaders)
    {
        var tenantId = http.Request.Headers[IdentityHeaders.Tenant].ToString();
        var userId = http.Request.Headers[IdentityHeaders.User].ToString();
        if (userId.Length == 0)
        {
            userId = "anonymous";
        }

        if (!SecurityTrimming.IsValidSegment(tenantId) || !SecurityTrimming.IsValidSegment(userId))
        {
            return TypedResults.BadRequest("A valid identity is required.");
        }

        principal = users.Resolve(tenantId, userId);
    }
    else
    {
        return TypedResults.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return TypedResults.BadRequest("A non-empty question is required.");
    }

    var response = await retriever.RetrieveAsync(principal, request, cancellationToken);

    // Context expansion around the best hit, when Cosmos is configured.
    var expander = http.RequestServices.GetService<NeighborExpander>();
    if (expander is not null)
    {
        response = new QueryResponse(
            await expander.ExpandTopHitAsync(principal.TenantId, response.Chunks, cancellationToken));
    }

    return TypedResults.Ok(response);
});

app.Run();