using System.Text.Json;

namespace KnowVault.Contracts.Messages;

/// <summary>
/// In Azure, Event Grid's BlobCreated subscription delivers its own schema to
/// the document-changed queue (locally, Admin's completion endpoint sends the
/// DocumentChanged contract directly). This maps the Event Grid payload onto
/// the contract so the Ingestion worker handles both without caring which
/// path produced the message.
/// </summary>
public static class EventGridBlobCreated
{
    public const string EventType = "Microsoft.Storage.BlobCreated";

    /// <summary>
    /// Tries to interpret a message body as an Event Grid BlobCreated event for
    /// an uploads-container blob shaped tenantId/documentId/fileName.
    /// </summary>
    public static bool TryMap(BinaryData body, out DocumentChanged? document)
    {
        document = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("eventType", out var eventType) ||
                eventType.GetString() != EventType ||
                !root.TryGetProperty("subject", out var subjectProperty))
            {
                return false;
            }

            // subject: /blobServices/default/containers/uploads/blobs/{tenant}/{doc}/{file}
            var subject = subjectProperty.GetString() ?? "";
            const string blobsMarker = "/blobs/";
            var markerIndex = subject.IndexOf(blobsMarker, StringComparison.Ordinal);
            if (markerIndex < 0 || !subject.Contains("/containers/uploads/", StringComparison.Ordinal))
            {
                return false;
            }

            var blobPath = subject[(markerIndex + blobsMarker.Length)..];
            var segments = blobPath.Split('/');
            if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            {
                return false;
            }

            var eventTime = root.TryGetProperty("eventTime", out var time) && time.TryGetDateTimeOffset(out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;
            var url = root.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var urlProperty)
                ? urlProperty.GetString()
                : null;

            document = new DocumentChanged(
                TenantId: segments[0],
                SourceId: "direct-upload",
                DocumentId: segments[1],
                SourceType: "upload",
                BlobPath: blobPath,
                SourceUrl: url,
                ContentHash: null,
                // The event carries no ACLs; Event Grid uploads are org-wide.
                // ACL-bearing uploads go through Admin's completion endpoint.
                AllowedPrincipals: [$"tenant:{segments[0]}:all"],
                DetectedAt: eventTime);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}