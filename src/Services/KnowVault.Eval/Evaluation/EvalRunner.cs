using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

using KnowVault.Contracts.Retrieval;

namespace KnowVault.Eval.Evaluation;

public sealed record QuestionResult(
    string Id,
    string Category,
    bool? Hit,
    double ReciprocalRank,
    bool RefusalCorrect,
    bool? SecurityPass,
    double? Groundedness,
    double? CitationAccuracy,
    double LatencyMs,
    string Answer);

public sealed record EvalReport(
    DateTimeOffset RanAt,
    int QuestionCount,
    double HitRateAt10,
    double MeanReciprocalRank,
    double RefusalCorrectness,
    double SecurityScore,
    double MeanGroundedness,
    double MeanCitationAccuracy,
    double LatencyP50Ms,
    double LatencyP95Ms,
    IReadOnlyList<QuestionResult> Results);

/// <summary>
/// Runs the golden set against the live pipeline. Retrieval metrics come from
/// the Query service, answer metrics from the Answer stream plus the LLM judge.
/// The security metric must be 1.0 — always — and gates CI hard in the next step.
/// </summary>
public sealed partial class EvalRunner(
    IHttpClientFactory httpClientFactory,
    AnswerClient answerClient,
    AnswerJudge judge,
    ILogger<EvalRunner> logger)
{
    private const string DefaultTenant = "eval";
    private const string DefaultUser = "reader";
    private const string RefusalMarker = "don't have information";

    private static readonly System.Text.Json.JsonSerializerOptions IndentedJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<EvalReport> RunAsync(CancellationToken cancellationToken)
    {
        var goldenSet = EvalFiles.LoadGoldenSet();
        var idMap = EvalFiles.LoadIdMap();
        if (idMap.Count == 0)
        {
            throw new InvalidOperationException("No seed id-map found — run /api/eval/seed first.");
        }

        var documentToLogical = idMap.ToDictionary(kv => kv.Value, kv => kv.Key);
        using var query = httpClientFactory.CreateClient("query");

        var results = new List<QuestionResult>();
        foreach (var question in goldenSet.Questions)
        {
            results.Add(await EvaluateAsync(question, query, documentToLogical, cancellationToken));
            LogQuestionDone(logger, question.Id, question.Category);
        }

        var report = Aggregate(results);
        await WriteReportAsync(report, cancellationToken);
        return report;
    }

    private async Task<QuestionResult> EvaluateAsync(
        GoldenQuestion question,
        HttpClient query,
        Dictionary<string, string> documentToLogical,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // Security questions run their main pass as the UNAUTHORIZED identity.
        var (tenant, user) = question.Security is { } spec
            ? (spec.UnauthorizedTenant, spec.UnauthorizedUser)
            : (DefaultTenant, DefaultUser);

        // --- retrieval metrics ---
        bool? hit = null;
        double reciprocalRank = 0;
        var chunks = await QueryAsAsync(query, tenant, user, question.Question, cancellationToken);

        if (question.ExpectedDocumentIds.Count > 0 && question.Security is null)
        {
            var ranks = chunks
                .Select((c, i) => (Logical: documentToLogical.GetValueOrDefault(c.DocumentId), Rank: i + 1))
                .Where(x => x.Logical is not null && question.ExpectedDocumentIds.Contains(x.Logical))
                .Select(x => x.Rank)
                .ToList();
            hit = ranks.Count > 0;
            reciprocalRank = ranks.Count > 0 ? 1.0 / ranks[0] : 0;
        }

        // --- answer + behavioral metrics ---
        var (answer, _) = await answerClient.AskAsync(tenant, user, question.Question, cancellationToken);
        stopwatch.Stop();

        var refused = answer.Contains(RefusalMarker, StringComparison.OrdinalIgnoreCase);
        var refusalCorrect = question.Category == "unanswerable" ? refused : !refused;

        bool? securityPass = null;
        if (question.Security is { } security)
        {
            // The unauthorized identity must see no marker and no restricted source...
            var leaked = security.ContentMarkers.Any(m => answer.Contains(m, StringComparison.OrdinalIgnoreCase)) ||
                         chunks.Any(c => documentToLogical.GetValueOrDefault(c.DocumentId) == security.RestrictedLogicalId);
            // ...and the authorized identity must actually get the content.
            var (authorizedAnswer, _) = await answerClient.AskAsync(
                security.AuthorizedTenant, security.AuthorizedUser, question.Question, cancellationToken);
            var authorizedOk = security.ContentMarkers.Any(m =>
                authorizedAnswer.Contains(m, StringComparison.OrdinalIgnoreCase));
            securityPass = !leaked && authorizedOk;
            refusalCorrect = refused; // the unauthorized run must refuse
        }

        // --- judge (answerable questions with sources only) ---
        double? groundedness = null, citationAccuracy = null;
        if (question.Category is "factual" or "multi-doc" && chunks.Count > 0 && !refused)
        {
            var verdict = await judge.JudgeAsync(question.Question, answer, chunks, cancellationToken);
            groundedness = verdict.Groundedness;
            citationAccuracy = verdict.CitationAccuracy;
        }

        return new QuestionResult(
            question.Id, question.Category, hit, reciprocalRank, refusalCorrect,
            securityPass, groundedness, citationAccuracy, stopwatch.Elapsed.TotalMilliseconds, answer);
    }

    private static async Task<IReadOnlyList<RetrievedChunk>> QueryAsAsync(
        HttpClient query, string tenant, string user, string question, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/query")
        {
            Content = JsonContent.Create(new QueryRequest(question, Top: 10)),
        };
        request.Headers.Add(IdentityHeaders.Tenant, tenant);
        request.Headers.Add(IdentityHeaders.User, user);

        using var response = await query.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QueryResponse>(cancellationToken))?.Chunks ?? [];
    }

    private static EvalReport Aggregate(List<QuestionResult> results)
    {
        var retrieval = results.Where(r => r.Hit.HasValue).ToList();
        var judged = results.Where(r => r.Groundedness.HasValue).ToList();
        var security = results.Where(r => r.SecurityPass.HasValue).ToList();
        var latencies = results.Select(r => r.LatencyMs).OrderBy(x => x).ToList();

        return new EvalReport(
            RanAt: DateTimeOffset.UtcNow,
            QuestionCount: results.Count,
            HitRateAt10: retrieval.Count == 0 ? 0 : retrieval.Count(r => r.Hit == true) / (double)retrieval.Count,
            MeanReciprocalRank: retrieval.Count == 0 ? 0 : retrieval.Average(r => r.ReciprocalRank),
            RefusalCorrectness: results.Count(r => r.RefusalCorrect) / (double)results.Count,
            SecurityScore: security.Count == 0 ? 1 : security.Count(r => r.SecurityPass == true) / (double)security.Count,
            MeanGroundedness: judged.Count == 0 ? 0 : judged.Average(r => r.Groundedness!.Value),
            MeanCitationAccuracy: judged.Count == 0 ? 0 : judged.Average(r => r.CitationAccuracy!.Value),
            LatencyP50Ms: Percentile(latencies, 0.50),
            LatencyP95Ms: Percentile(latencies, 0.95),
            Results: results);
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

    private async Task WriteReportAsync(EvalReport report, CancellationToken cancellationToken)
    {
        var resultsDir = Path.Combine(EvalFiles.EvalsDirectory, "results");
        Directory.CreateDirectory(resultsDir);
        var stamp = report.RanAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        await File.WriteAllTextAsync(
            Path.Combine(resultsDir, $"eval-{stamp}.json"),
            JsonSerializer.Serialize(report, IndentedJson),
            cancellationToken);

        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Eval run — {report.RanAt:yyyy-MM-dd HH:mm} UTC");
        md.AppendLine();
        md.AppendLine("| Metric | Value | Gate |");
        md.AppendLine("|---|---|---|");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Security (isolation) | {report.SecurityScore:P0} | must be 100% |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Retrieval hit-rate@10 | {report.HitRateAt10:P0} | no >2pt drop |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Mean reciprocal rank | {report.MeanReciprocalRank:F3} | informational |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Refusal correctness | {report.RefusalCorrectness:P0} | no >2pt drop |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Groundedness (judge) | {report.MeanGroundedness:P0} | no >2pt drop |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Citation accuracy (judge) | {report.MeanCitationAccuracy:P0} | informational |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Latency p50 / p95 | {report.LatencyP50Ms:F0} ms / {report.LatencyP95Ms:F0} ms | informational |");
        md.AppendLine();
        md.AppendLine("| Question | Category | Hit | RR | Refusal ok | Security | Grounded | Citations | Latency |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in report.Results)
        {
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.Id} | {r.Category} | {Fmt(r.Hit)} | {r.ReciprocalRank:F2} | {(r.RefusalCorrect ? "yes" : "NO")} | {Fmt(r.SecurityPass)} | {FmtPct(r.Groundedness)} | {FmtPct(r.CitationAccuracy)} | {r.LatencyMs:F0} ms |");
        }

        await File.WriteAllTextAsync(Path.Combine(resultsDir, $"eval-{stamp}.md"), md.ToString(), cancellationToken);
        LogReportWritten(logger, resultsDir, stamp);

        static string Fmt(bool? b) => b switch { true => "yes", false => "NO", null => "–" };
        static string FmtPct(double? d) => d.HasValue ? d.Value.ToString("P0", CultureInfo.InvariantCulture) : "–";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluated {QuestionId} ({Category})")]
    private static partial void LogQuestionDone(ILogger logger, string questionId, string category);

    [LoggerMessage(Level = LogLevel.Information, Message = "Eval report written to {ResultsDir} (eval-{Stamp}.*)")]
    private static partial void LogReportWritten(ILogger logger, string resultsDir, string stamp);
}