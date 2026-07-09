using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Azure.AI.OpenAI;

using KnowVault.Contracts.Retrieval;

using OpenAI.Chat;

namespace KnowVault.Eval.Evaluation;

public sealed record JudgeVerdict(double Groundedness, double CitationAccuracy);

/// <summary>
/// LLM-as-judge for groundedness and citation accuracy. Currently gpt-5-mini
/// (the only deployed model); the plan's dedicated larger judge model arrives
/// when eval volume justifies it — a deliberate, documented compromise.
/// </summary>
public sealed partial class AnswerJudge(AzureOpenAIClient openAiClient, IConfiguration configuration)
{
    private readonly ChatClient _chat =
        openAiClient.GetChatClient(configuration["Azure:OpenAI:JudgeDeployment"] ?? "gpt-5-mini");

    public async Task<JudgeVerdict> JudgeAsync(
        string question, string answer, IReadOnlyList<RetrievedChunk> chunks, CancellationToken cancellationToken)
    {
        var sources = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            sources.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"[{i + 1}] {chunks[i].Content}");
        }

        List<ChatMessage> messages =
        [
            new SystemChatMessage("""
                You are a strict evaluator of retrieval-augmented answers. Score two things:
                - groundedness: fraction of the answer's factual claims that are directly supported by the sources (0.0-1.0).
                - citation_accuracy: fraction of the answer's [n] citations that point to a source which actually contains the cited claim (0.0-1.0; if there are no citations at all, score 0.0 unless the answer is a refusal).
                Reply with ONLY a JSON object: {"groundedness": <number>, "citation_accuracy": <number>}
                """),
            new UserChatMessage($"""
                Sources:
                {sources}
                Question: {question}

                Answer to evaluate:
                {answer}
                """),
        ];

        var completion = await _chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var text = completion.Value.Content[0].Text;

        try
        {
            using var parsed = JsonDocument.Parse(ExtractJson(text));
            return new JudgeVerdict(
                Groundedness: parsed.RootElement.GetProperty("groundedness").GetDouble(),
                CitationAccuracy: parsed.RootElement.GetProperty("citation_accuracy").GetDouble());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            // A judge that can't produce parseable output scores conservatively.
            return new JudgeVerdict(0, 0);
        }
    }

    private static string ExtractJson(string text)
    {
        var match = JsonObject().Match(text);
        return match.Success ? match.Value : text;
    }

    [GeneratedRegex(@"\{[^{}]*\}", RegexOptions.Singleline)]
    private static partial Regex JsonObject();
}