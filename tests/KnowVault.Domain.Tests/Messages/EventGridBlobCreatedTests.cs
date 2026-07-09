using KnowVault.Contracts.Messages;

namespace KnowVault.Domain.Tests.Messages;

public class EventGridBlobCreatedTests
{
    private static BinaryData Event(string subject, string eventType = "Microsoft.Storage.BlobCreated") =>
        BinaryData.FromString($$"""
            {
              "id": "evt-1",
              "topic": "/subscriptions/x/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/st",
              "subject": "{{subject}}",
              "eventType": "{{eventType}}",
              "eventTime": "2026-07-09T10:00:00Z",
              "data": { "url": "https://st.blob.core.windows.net/uploads/acme/doc42/report.md" }
            }
            """);

    [Fact]
    public void Maps_blob_created_event_to_document_changed()
    {
        var ok = EventGridBlobCreated.TryMap(
            Event("/blobServices/default/containers/uploads/blobs/acme/doc42/report.md"), out var document);

        Assert.True(ok);
        Assert.NotNull(document);
        Assert.Equal("acme", document.TenantId);
        Assert.Equal("doc42", document.DocumentId);
        Assert.Equal("acme/doc42/report.md", document.BlobPath);
        Assert.Equal("upload", document.SourceType);
        Assert.Equal(["tenant:acme:all"], document.AllowedPrincipals);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero), document.DetectedAt);
    }

    [Fact]
    public void Rejects_other_event_types()
    {
        var ok = EventGridBlobCreated.TryMap(
            Event("/blobServices/default/containers/uploads/blobs/a/b/c.md", "Microsoft.Storage.BlobDeleted"),
            out var document);

        Assert.False(ok);
        Assert.Null(document);
    }

    [Fact]
    public void Rejects_blobs_outside_the_uploads_container()
    {
        var ok = EventGridBlobCreated.TryMap(
            Event("/blobServices/default/containers/other/blobs/a/b/c.md"), out _);

        Assert.False(ok);
    }

    [Fact]
    public void Rejects_unexpected_blob_path_shapes()
    {
        var ok = EventGridBlobCreated.TryMap(
            Event("/blobServices/default/containers/uploads/blobs/no-folders.md"), out _);

        Assert.False(ok);
    }

    [Fact]
    public void Rejects_document_changed_contract_bodies()
    {
        var body = BinaryData.FromString("""{"tenantId":"t","documentId":"d","sourceType":"upload"}""");

        Assert.False(EventGridBlobCreated.TryMap(body, out _));
    }

    [Fact]
    public void Rejects_non_json()
    {
        Assert.False(EventGridBlobCreated.TryMap(BinaryData.FromString("not json"), out _));
    }
}