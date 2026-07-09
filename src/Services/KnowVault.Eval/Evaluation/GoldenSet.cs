using System.Text.Json;

namespace KnowVault.Eval.Evaluation;

public sealed record GoldenQuestion(
    string Id,
    string Category,
    string Question,
    IReadOnlyList<string> ExpectedDocumentIds,
    string ReferenceAnswer,
    SecuritySpec? Security,
    string? Notes);

/// <summary>Data-driven security check: ask as both identities, compare against the markers.</summary>
public sealed record SecuritySpec(
    string AuthorizedTenant,
    string AuthorizedUser,
    string UnauthorizedTenant,
    string UnauthorizedUser,
    IReadOnlyList<string> ContentMarkers,
    string RestrictedLogicalId);

public sealed record GoldenSet(int Version, string Corpus, IReadOnlyList<GoldenQuestion> Questions);

public sealed record SeedDocument(
    string LogicalId,
    string File,
    string Tenant,
    string Probe,
    string? ProbeUser = null,
    IReadOnlyList<string>? AllowedPrincipals = null);

public sealed record SeedManifest(IReadOnlyList<SeedDocument> Documents);

/// <summary>
/// Locates the repo's evals directory (EVALS_DIR from the AppHost, or an
/// upward search from the binary for direct `dotnet run`) and loads its files.
/// </summary>
public static class EvalFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions IndentedJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string EvalsDirectory { get; } = Resolve();

    public static GoldenSet LoadGoldenSet() =>
        JsonSerializer.Deserialize<GoldenSet>(
            File.ReadAllText(Path.Combine(EvalsDirectory, "golden-questions.json")), Json)
        ?? throw new InvalidOperationException("golden-questions.json is empty.");

    public static SeedManifest LoadSeedManifest() =>
        JsonSerializer.Deserialize<SeedManifest>(
            File.ReadAllText(Path.Combine(EvalsDirectory, "corpus", "seed-manifest.json")), Json)
        ?? throw new InvalidOperationException("seed-manifest.json is empty.");

    public static string CorpusPath(string file) => Path.Combine(EvalsDirectory, "corpus", file);

    /// <summary>logical id → uploaded document id, produced by seeding, environment-local.</summary>
    public static string IdMapPath => Path.Combine(EvalsDirectory, ".idmap.dev.json");

    public static Dictionary<string, string> LoadIdMap() =>
        File.Exists(IdMapPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(IdMapPath), Json) ?? []
            : [];

    public static void SaveIdMap(Dictionary<string, string> map) =>
        File.WriteAllText(IdMapPath, JsonSerializer.Serialize(map, IndentedJson));

    private static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("EVALS_DIR");
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "evals");
            if (File.Exists(Path.Combine(candidate, "golden-questions.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not locate the evals directory; set EVALS_DIR.");
    }
}