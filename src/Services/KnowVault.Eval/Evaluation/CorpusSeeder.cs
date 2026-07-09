using System.Text.Json;

using KnowVault.Contracts.Retrieval;

namespace KnowVault.Eval.Evaluation;

/// <summary>
/// Seeds the fixture corpus through the REAL pipeline: Admin SAS upload →
/// blob → queue → Ingestion → index. Skips documents whose previous seeding
/// is still retrievable, so repeated seeds don't duplicate content.
/// </summary>
public sealed partial class CorpusSeeder(IHttpClientFactory httpClientFactory, ILogger<CorpusSeeder> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Dictionary<string, string>> SeedAsync(CancellationToken cancellationToken)
    {
        var manifest = EvalFiles.LoadSeedManifest();
        var idMap = EvalFiles.LoadIdMap();
        using var admin = httpClientFactory.CreateClient("admin");
        using var query = httpClientFactory.CreateClient("query");
        using var blobHttp = httpClientFactory.CreateClient();

        foreach (var doc in manifest.Documents)
        {
            if (idMap.TryGetValue(doc.LogicalId, out var existingId) &&
                await IsRetrievableAsync(query, doc, existingId, cancellationToken))
            {
                LogSkipped(logger, doc.LogicalId);
                continue;
            }

            // 1. SAS URL
            var createResponse = await admin.PostAsJsonAsync(
                $"/api/tenants/{doc.Tenant}/documents", new { fileName = doc.File }, cancellationToken);
            createResponse.EnsureSuccessStatusCode();
            var created = JsonSerializer.Deserialize<JsonElement>(
                await createResponse.Content.ReadAsStringAsync(cancellationToken), Json);
            var documentId = created.GetProperty("documentId").GetString()!;
            var uploadUrl = created.GetProperty("uploadUrl").GetString()!;

            // 2. PUT the file straight to blob storage
            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(EvalFiles.CorpusPath(doc.File), cancellationToken));
            content.Headers.Add("x-ms-blob-type", "BlockBlob");
            using var putRequest = new HttpRequestMessage(HttpMethod.Put, new Uri(uploadUrl)) { Content = content };
            (await blobHttp.SendAsync(putRequest, cancellationToken)).EnsureSuccessStatusCode();

            // 3. Complete → DocumentChanged → ingestion
            var completeResponse = await admin.PostAsJsonAsync(
                $"/api/tenants/{doc.Tenant}/documents/{documentId}/complete", new { fileName = doc.File }, cancellationToken);
            completeResponse.EnsureSuccessStatusCode();

            idMap[doc.LogicalId] = documentId;
            LogUploaded(logger, doc.LogicalId, documentId, doc.Tenant);
        }

        // 4. Wait until every document is retrievable via its probe phrase.
        foreach (var doc in manifest.Documents)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            while (!await IsRetrievableAsync(query, doc, idMap[doc.LogicalId], cancellationToken))
            {
                if (DateTimeOffset.UtcNow > deadline)
                {
                    throw new TimeoutException($"Document '{doc.LogicalId}' was not retrievable within 90s of seeding.");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }

            LogRetrievable(logger, doc.LogicalId);
        }

        EvalFiles.SaveIdMap(idMap);
        return idMap;
    }

    private static async Task<bool> IsRetrievableAsync(
        HttpClient query, SeedDocument doc, string documentId, CancellationToken cancellationToken)
    {
        var response = await query.PostAsJsonAsync(
            "/api/query", new QueryRequest(doc.Tenant, doc.Probe, Top: 5), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<QueryResponse>(cancellationToken);
        return result is not null && result.Chunks.Any(c => c.DocumentId == documentId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Corpus doc {LogicalId} already retrievable; skipping upload")]
    private static partial void LogSkipped(ILogger logger, string logicalId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded {LogicalId} as {DocumentId} (tenant {Tenant})")]
    private static partial void LogUploaded(ILogger logger, string logicalId, string documentId, string tenant);

    [LoggerMessage(Level = LogLevel.Information, Message = "Corpus doc {LogicalId} is retrievable")]
    private static partial void LogRetrievable(ILogger logger, string logicalId);
}