using System.Runtime.CompilerServices;
using System.Text;

using Azure.AI.OpenAI;

using KnowVault.Contracts.Retrieval;

using OpenAI.Chat;

namespace KnowVault.Answer.Answering;

/// <summary>
/// Grounded generation: retrieve via the Query service, build a strict
/// answer-only-from-sources prompt, and stream tokens. The refusal line is
/// part of the contract — tested explicitly by the eval suite in Phase 4.
/// </summary>
public sealed partial class GroundedAnswerer(
    IHttpClientFactory httpClientFactory,
    AzureOpenAIClient openAiClient,
    IConfiguration configuration,
    ILogger<GroundedAnswerer> logger)
{
    public const string RefusalLine = "I don't have information on that.";

    private readonly ChatClient _chat =
        openAiClient.GetChatClient(configuration["Azure:OpenAI:GenerationDeployment"] ?? "gpt-5-mini");

    public async Task<(IReadOnlyList<AnswerSource> Sources, IReadOnlyList<RetrievedChunk> Chunks)> RetrieveSourcesAsync(
        string tenantId, string userId, AskRequest request, CancellationToken cancellationToken)
    {
        using var query = httpClientFactory.CreateClient("query");

        // Propagate the caller's identity so trimming happens as THEM, not as us.
        using var queryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/query")
        {
            Content = JsonContent.Create(new QueryRequest(request.Question)),
        };
        queryRequest.Headers.Add(IdentityHeaders.Tenant, tenantId);
        queryRequest.Headers.Add(IdentityHeaders.User, userId);

        using var response = await query.SendAsync(queryRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<QueryResponse>(cancellationToken)
            ?? new QueryResponse([]);

        var sources = result.Chunks
            .Select((c, i) => new AnswerSource(i + 1, c.ChunkId, c.DocumentId, c.Title, c.SourceUrl))
            .ToList();

        LogSources(logger, sources.Count, tenantId, userId);
        return (sources, result.Chunks);
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var context = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            context.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"[{i + 1}] {chunks[i].Title}");
            context.AppendLine(chunks[i].Content);
            context.AppendLine();
        }

        List<ChatMessage> messages =
        [
            new SystemChatMessage($"""
                You are KnowVault, an assistant that answers questions strictly from the numbered sources provided.
                Rules:
                - Use ONLY the sources below. Never use outside knowledge.
                - Cite every claim with the source number in square brackets, like [1] or [2][3].
                - If the sources do not contain the answer, reply exactly: "{RefusalLine}"
                - Be concise and factual.
                """),
            new UserChatMessage($"""
                Sources:
                {context}
                Question: {question}
                """),
        ];

        await foreach (var update in _chat.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {SourceCount} sources for answer (tenant {TenantId}, user {UserId})")]
    private static partial void LogSources(ILogger logger, int sourceCount, string tenantId, string userId);
}