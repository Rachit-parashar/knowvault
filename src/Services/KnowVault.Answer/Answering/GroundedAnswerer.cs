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
    UsageMetrics usage,
    ILogger<GroundedAnswerer> logger)
{
    public const string RefusalLine = "I don't have information on that.";

    private readonly ChatClient _chat =
        openAiClient.GetChatClient(configuration["Azure:OpenAI:GenerationDeployment"] ?? "gpt-5-mini");

    /// <summary>
    /// Follow-up questions retrieve poorly verbatim ("what about the premium
    /// tier?"), so with history present the question is rewritten standalone
    /// before retrieval. Skipped entirely on first turns (plan §6 step 1).
    /// </summary>
    public async Task<string> RewriteQuestionAsync(
        string question, IReadOnlyList<ChatTurn>? history, CancellationToken cancellationToken)
    {
        if (history is not { Count: > 0 })
        {
            return question;
        }

        var transcript = new StringBuilder();
        foreach (var turn in history.TakeLast(3))
        {
            transcript.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"User: {turn.Question}");
            transcript.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Assistant: {turn.Answer}");
        }

        List<ChatMessage> messages =
        [
            new SystemChatMessage("""
                Rewrite the user's latest question as a single fully self-contained question:
                resolve pronouns and references using the conversation, expand acronyms the
                conversation defines, and change nothing else. Reply with ONLY the rewritten question.
                """),
            new UserChatMessage($"""
                Conversation:
                {transcript}
                Latest question: {question}
                """),
        ];

        var completion = await _chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var rewritten = completion.Value.Content[0].Text.Trim();
        return rewritten.Length is > 0 and < 500 ? rewritten : question;
    }

    public async Task<(IReadOnlyList<AnswerSource> Sources, IReadOnlyList<RetrievedChunk> Chunks)> RetrieveSourcesAsync(
        string tenantId, string userId, string? bearerToken, string searchQuestion, CancellationToken cancellationToken)
    {
        using var query = httpClientFactory.CreateClient("query");

        // Propagate the caller's identity so trimming happens as THEM, not as us:
        // the original bearer token when signed in (plan §5b), dev headers otherwise.
        using var queryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/query")
        {
            Content = JsonContent.Create(new QueryRequest(searchQuestion)),
        };
        if (bearerToken is not null)
        {
            queryRequest.Headers.Authorization = new("Bearer", bearerToken);
        }
        else
        {
            queryRequest.Headers.Add(IdentityHeaders.Tenant, tenantId);
            queryRequest.Headers.Add(IdentityHeaders.User, userId);
        }

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
        string tenantId,
        string question,
        IReadOnlyList<ChatTurn>? history,
        IReadOnlyList<RetrievedChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var completion = new StringBuilder();
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
        ];

        // Recent turns give the model conversational continuity; grounding
        // still comes exclusively from the sources in the final message.
        foreach (var turn in (history ?? []).TakeLast(3))
        {
            messages.Add(new UserChatMessage(turn.Question));
            messages.Add(new AssistantChatMessage(turn.Answer));
        }

        messages.Add(new UserChatMessage($"""
            Sources:
            {context}
            Question: {question}
            """));

        await foreach (var update in _chat.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    completion.Append(part.Text);
                    yield return part.Text;
                }
            }
        }

        stopwatch.Stop();
        var promptText = string.Join("\n", messages.SelectMany(m => m.Content).Select(c => c.Text));
        usage.RecordAnswer(tenantId, promptText, completion.ToString(), stopwatch.Elapsed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {SourceCount} sources for answer (tenant {TenantId}, user {UserId})")]
    private static partial void LogSources(ILogger logger, int sourceCount, string tenantId, string userId);
}