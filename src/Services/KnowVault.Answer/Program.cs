using System.Text.Json;

using Azure.AI.OpenAI;
using Azure.Identity;

using KnowVault.Answer.Answering;
using KnowVault.Contracts.Retrieval;

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

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/answer", async (
    AskRequest request,
    GroundedAnswerer answerer,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.Request.Headers[IdentityHeaders.Tenant].ToString();
    var userId = http.Request.Headers[IdentityHeaders.User].ToString();
    if (userId.Length == 0)
    {
        userId = "anonymous";
    }

    if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(request.Question))
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsync("identity headers and a question are required.", cancellationToken);
        return;
    }

    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    var (sources, chunks) = await answerer.RetrieveSourcesAsync(tenantId, userId, request, cancellationToken);

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