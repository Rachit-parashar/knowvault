using System.Diagnostics.Metrics;

using KnowVault.Domain.Chunking;

namespace KnowVault.Answer.Answering;

/// <summary>
/// Per-answer usage accounting (success criterion 5: cost visibility).
/// Token counts are tokenizer estimates (cl100k_base) — close enough for cost
/// tracking; exact billed counts come from Azure OpenAI invoices. Prices are
/// configuration so model changes don't need code changes.
/// </summary>
public sealed partial class UsageMetrics
{
    private static readonly Meter Meter = new("KnowVault.Answer");
    private static readonly Cl100kTokenCounter TokenCounter = new();

    private readonly Counter<long> _promptTokens =
        Meter.CreateCounter<long>("knowvault.tokens.prompt", "tokens");
    private readonly Counter<long> _completionTokens =
        Meter.CreateCounter<long>("knowvault.tokens.completion", "tokens");
    private readonly Counter<double> _cost =
        Meter.CreateCounter<double>("knowvault.cost.usd", "USD");
    private readonly Histogram<double> _duration =
        Meter.CreateHistogram<double>("knowvault.answer.duration", "ms");

    private readonly double _promptPricePerMTokens;
    private readonly double _completionPricePerMTokens;
    private readonly ILogger<UsageMetrics> _logger;

    public UsageMetrics(IConfiguration configuration, ILogger<UsageMetrics> logger)
    {
        _logger = logger;
        _promptPricePerMTokens = configuration.GetValue("Azure:OpenAI:PromptPricePerMTokens", 0.25);
        _completionPricePerMTokens = configuration.GetValue("Azure:OpenAI:CompletionPricePerMTokens", 2.00);
    }

    public void RecordAnswer(string tenantId, string promptText, string completionText, TimeSpan duration)
    {
        var promptTokens = TokenCounter.Count(promptText);
        var completionTokens = TokenCounter.Count(completionText);
        var cost = (promptTokens * _promptPricePerMTokens + completionTokens * _completionPricePerMTokens) / 1_000_000;

        var tenantTag = new KeyValuePair<string, object?>("tenant", tenantId);
        _promptTokens.Add(promptTokens, tenantTag);
        _completionTokens.Add(completionTokens, tenantTag);
        _cost.Add(cost, tenantTag);
        _duration.Record(duration.TotalMilliseconds, tenantTag);

        // The usage ledger line — queryable in App Insights, feeds the per-tenant cost story.
        LogUsage(_logger, tenantId, promptTokens, completionTokens, cost, duration.TotalMilliseconds);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Usage: tenant {TenantId} prompt {PromptTokens} + completion {CompletionTokens} tokens = ${CostUsd:F6} in {DurationMs:F0}ms")]
    private static partial void LogUsage(
        ILogger logger, string tenantId, int promptTokens, int completionTokens, double costUsd, double durationMs);
}