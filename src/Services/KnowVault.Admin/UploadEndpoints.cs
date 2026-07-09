using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

using KnowVault.Contracts.Messages;

using Microsoft.AspNetCore.Http.HttpResults;

namespace KnowVault.Admin;

/// <summary>
/// Direct-upload flow: the client asks for a short-lived SAS URL, PUTs the
/// file straight to Blob storage, then reports completion — at which point a
/// DocumentChanged message kicks off ingestion. In Azure, Event Grid's
/// BlobCreated subscription replaces the completion call (Phase 1 infra).
/// </summary>
public static class UploadEndpoints
{
    private static readonly string[] AllowedExtensions = [".md", ".txt", ".pdf"];

    private static readonly TimeSpan SasLifetime = TimeSpan.FromMinutes(15);

    public static void MapUploadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId}/documents");

        group.MapPost("/", CreateUpload);
        group.MapPost("/{documentId}/complete", CompleteUpload);
    }

    private static Results<Ok<CreateUploadResponse>, BadRequest<string>> CreateUpload(
        string tenantId,
        CreateUploadRequest request,
        BlobContainerClient uploads)
    {
        if (!IsSafeSegment(tenantId))
        {
            return TypedResults.BadRequest("Invalid tenant id.");
        }

        var fileName = Path.GetFileName(request.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (fileName.Length == 0 || fileName != request.FileName || !AllowedExtensions.Contains(extension))
        {
            return TypedResults.BadRequest($"File name must be a bare name with one of: {string.Join(", ", AllowedExtensions)}");
        }

        var documentId = Guid.NewGuid().ToString("N");
        var blobPath = $"{tenantId}/{documentId}/{fileName}";
        var blobClient = uploads.GetBlobClient(blobPath);

        if (!blobClient.CanGenerateSasUri)
        {
            // Managed Identity path needs user-delegation SAS — wired up with the
            // real Storage account in Azure; Azurite always has a shared key.
            return TypedResults.BadRequest("This environment cannot issue upload URLs.");
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(SasLifetime);
        var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expiresAt);

        return TypedResults.Ok(new CreateUploadResponse(documentId, blobPath, sasUri, expiresAt));
    }

    private static async Task<Results<Accepted, BadRequest<string>, NotFound<string>>> CompleteUpload(
        string tenantId,
        string documentId,
        CompleteUploadRequest request,
        BlobContainerClient uploads,
        ServiceBusClient messaging,
        CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(tenantId) || !IsSafeSegment(documentId))
        {
            return TypedResults.BadRequest("Invalid tenant or document id.");
        }

        var fileName = Path.GetFileName(request.FileName);
        var blobPath = $"{tenantId}/{documentId}/{fileName}";

        if (!await uploads.GetBlobClient(blobPath).ExistsAsync(cancellationToken))
        {
            return TypedResults.NotFound($"No uploaded blob at {blobPath}.");
        }

        // Per-document ACLs: caller-supplied principals (user:/group:) or org-wide default.
        var allowedPrincipals = request.AllowedPrincipals is { Count: > 0 }
            ? request.AllowedPrincipals
            : [$"tenant:{tenantId}:all"];
        if (allowedPrincipals.Any(p => !IsValidPrincipal(p, tenantId)))
        {
            return TypedResults.BadRequest(
                "allowedPrincipals entries must be 'user:<id>', 'group:<id>', or 'tenant:<tenantId>:all'.");
        }

        var message = new DocumentChanged(
            TenantId: tenantId,
            SourceId: "direct-upload",
            DocumentId: documentId,
            SourceType: "upload",
            BlobPath: blobPath,
            SourceUrl: null,
            ContentHash: null,
            AllowedPrincipals: allowedPrincipals,
            DetectedAt: DateTimeOffset.UtcNow);

        var sender = messaging.CreateSender("document-changed");
        await using (sender.ConfigureAwait(false))
        {
            var busMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(message))
            {
                ContentType = "application/json",
                Subject = MessageContracts.DocumentChangedV1,
                MessageId = $"{tenantId}:{documentId}",
            };
            await sender.SendMessageAsync(busMessage, cancellationToken);
        }

        return TypedResults.Accepted($"/api/tenants/{tenantId}/documents/{documentId}");
    }

    private static bool IsSafeSegment(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsValidPrincipal(string principal, string tenantId) =>
        principal == $"tenant:{tenantId}:all" ||
        (principal.StartsWith("user:", StringComparison.Ordinal) &&
         KnowVault.Domain.Security.SecurityTrimming.IsValidSegment(principal["user:".Length..])) ||
        (principal.StartsWith("group:", StringComparison.Ordinal) &&
         KnowVault.Domain.Security.SecurityTrimming.IsValidSegment(principal["group:".Length..]));
}

public sealed record CreateUploadRequest(string FileName);

public sealed record CreateUploadResponse(string DocumentId, string BlobPath, Uri UploadUrl, DateTimeOffset ExpiresAt);

public sealed record CompleteUploadRequest(string FileName, IReadOnlyList<string>? AllowedPrincipals = null);