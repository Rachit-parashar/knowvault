using Azure.AI.OpenAI;
using Azure.Identity;

using KnowVault.Eval.Evaluation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("admin", client => client.BaseAddress = new Uri("https+http://admin"));
builder.Services.AddHttpClient("query", client => client.BaseAddress = new Uri("https+http://query"));
builder.Services.AddHttpClient("answer", client =>
{
    client.BaseAddress = new Uri("https+http://answer");
    client.Timeout = TimeSpan.FromMinutes(3); // streamed generation
});

var openAiEndpoint = builder.Configuration["Azure:OpenAI:Endpoint"];
if (!string.IsNullOrEmpty(openAiEndpoint))
{
    builder.Services.AddSingleton(new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential()));
    builder.Services.AddSingleton<AnswerJudge>();
    builder.Services.AddSingleton<AnswerClient>();
    builder.Services.AddSingleton<CorpusSeeder>();
    builder.Services.AddSingleton<EvalRunner>();
}

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "KnowVault.Eval");

app.MapPost("/api/eval/seed", async (CorpusSeeder seeder, CancellationToken cancellationToken) =>
    Results.Ok(await seeder.SeedAsync(cancellationToken)));

app.MapPost("/api/eval/run", async (EvalRunner runner, CancellationToken cancellationToken) =>
    Results.Ok(await runner.RunAsync(cancellationToken)));

app.Run();