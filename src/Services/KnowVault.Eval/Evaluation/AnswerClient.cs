using System.Text;
using System.Text.Json;

using KnowVault.Contracts.Retrieval;

namespace KnowVault.Eval.Evaluation;

/// <summary>Consumes the Answer service's SSE stream and reassembles the full answer + sources.</summary>
public sealed class AnswerClient(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(string Answer, IReadOnlyList<AnswerSource> Sources)> AskAsync(
        string tenantId, string question, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("answer");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/answer")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new AskRequest(tenantId, question), Json),
                Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var answer = new StringBuilder();
        IReadOnlyList<AnswerSource> sources = [];
        string? currentEvent = null;

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEvent = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line["data: ".Length..];
                switch (currentEvent)
                {
                    case "sources":
                        sources = JsonSerializer.Deserialize<List<AnswerSource>>(data, Json) ?? [];
                        break;
                    case "token":
                        answer.Append(JsonSerializer.Deserialize<string>(data, Json));
                        break;
                }
            }
        }

        return (answer.ToString(), sources);
    }
}