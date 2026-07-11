using System.Text.Json;

using Azure.AI.OpenAI;
using Azure.Identity;

using KnowVault.Answer.Answering;
using KnowVault.Contracts.Retrieval;

using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Resolved by Aspire service discovery locally and Container Apps DNS in Azure.
builder.Services.AddHttpClient("query", client => client.BaseAddress = new Uri("https+http://query"));

var openAiEndpoint = builder.Configuration["Azure:OpenAI:Endpoint"];
if (!string.IsNullOrEmpty(openAiEndpoint))
{
    builder.Services.AddSingleton(new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential()));
    builder.Services.AddSingleton<UsageMetrics>();
    builder.Services.AddSingleton<GroundedAnswerer>();
}

// Entra ID (JWT) validation when configured; AllowDevHeaders keeps the
// header fallback for local dev and CI. See Query's Program.cs for the pair.
var entraTenantId = builder.Configuration["Entra:TenantId"];
var entraClientId = builder.Configuration["Entra:ClientId"];
var entraEnabled = !string.IsNullOrEmpty(entraTenantId) && !string.IsNullOrEmpty(entraClientId);
var allowDevHeaders = !entraEnabled || builder.Configuration.GetValue("Entra:AllowDevHeaders", false);
var appTenant = builder.Configuration["Entra:AppTenant"] ?? "eval";

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

app.UseDefaultFiles();
app.UseStaticFiles();

// Sign-in configuration for the chat page; empty clientId means dev mode.
app.MapGet("/api/config", () => Results.Ok(new
{
    clientId = entraClientId ?? "",
    tenantId = entraTenantId ?? "",
    scope = entraEnabled ? $"api://{entraClientId}/access_as_user" : "",
}));

app.MapPost("/api/answer", async (
    AskRequest request,
    GroundedAnswerer answerer,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    string tenantId;
    string userId;
    string? bearer = null;

    if (http.User.Identity?.IsAuthenticated == true)
    {
        tenantId = appTenant;
        userId = http.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? http.User.FindFirst("oid")?.Value ?? "unknown";
        var auth = http.Request.Headers.Authorization.ToString();
        bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..] : null;
    }
    else if (allowDevHeaders)
    {
        tenantId = http.Request.Headers[IdentityHeaders.Tenant].ToString();
        userId = http.Request.Headers[IdentityHeaders.User].ToString();
        if (userId.Length == 0)
        {
            userId = "anonymous";
        }
    }
    else
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(request.Question))
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsync("identity and a question are required.", cancellationToken);
        return;
    }

    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    var (sources, chunks) = await answerer.RetrieveSourcesAsync(tenantId, userId, bearer, request, cancellationToken);

    // First event: the numbered sources the client resolves [n] markers against.
    await WriteEventAsync(http.Response, "sources",
        JsonSerializer.Serialize(sources, JsonSerializerOptions.Web), cancellationToken);

    if (chunks.Count == 0)
    {
        await WriteEventAsync(http.Response, "token",
            JsonSerializer.Serialize(GroundedAnswerer.RefusalLine), cancellationToken);
    }
    else
    {
        await foreach (var token in answerer.StreamAnswerAsync(tenantId, request.Question, chunks, cancellationToken))
        {
            await WriteEventAsync(http.Response, "token", JsonSerializer.Serialize(token), cancellationToken);
        }
    }

    await WriteEventAsync(http.Response, "done", "{}", cancellationToken);
});

app.Run();

static async Task WriteEventAsync(HttpResponse response, string eventName, string data, CancellationToken cancellationToken)
{
    await response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}