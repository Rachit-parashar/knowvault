using KnowVault.Connector.Sync;

namespace KnowVault.Domain.Tests.Sync;

public class GoogleDriveMappingTests
{
    [Fact]
    public void Native_google_doc_exports_as_markdown()
    {
        var mapping = GoogleDriveMapping.TryMapFile("Q3 Plan", GoogleDriveMapping.DocMimeType, null, 17);

        Assert.NotNull(mapping);
        Assert.Equal("Q3-Plan.md", mapping.FileName);
        Assert.Equal("v17", mapping.ContentHash);
        Assert.Equal("text/markdown", mapping.ExportMimeType);
    }

    [Fact]
    public void Sheet_exports_as_csv_text()
    {
        var mapping = GoogleDriveMapping.TryMapFile("Budget 2027", GoogleDriveMapping.SheetMimeType, null, 3);

        Assert.NotNull(mapping);
        Assert.Equal("Budget-2027.txt", mapping.FileName);
        Assert.Equal("text/csv", mapping.ExportMimeType);
    }

    [Theory]
    [InlineData("notes.md", "text/markdown")]
    [InlineData("report.PDF", "application/pdf")]
    [InlineData("readme.txt", "text/plain")]
    public void Supported_binaries_download_directly_with_md5_hash(string name, string mime)
    {
        var mapping = GoogleDriveMapping.TryMapFile(name, mime, "abc123", 5);

        Assert.NotNull(mapping);
        Assert.Null(mapping.ExportMimeType);
        Assert.Equal("abc123", mapping.ContentHash); // md5 wins over version
        Assert.EndsWith(Path.GetExtension(name).ToLowerInvariant(), mapping.FileName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("video.mp4", "video/mp4")]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("slides", "application/vnd.google-apps.presentation")]
    public void Unsupported_types_are_skipped(string name, string mime)
    {
        Assert.Null(GoogleDriveMapping.TryMapFile(name, mime, "abc", 1));
    }

    [Fact]
    public void File_without_hash_or_version_is_skipped()
    {
        Assert.Null(GoogleDriveMapping.TryMapFile("x.md", "text/markdown", null, null));
    }

    [Fact]
    public void File_names_are_sanitized_for_blob_paths()
    {
        var mapping = GoogleDriveMapping.TryMapFile("Q3 / Plan: final?", GoogleDriveMapping.DocMimeType, null, 1);

        Assert.NotNull(mapping);
        Assert.Equal("Q3---Plan--final.md", mapping.FileName);
    }

    [Fact]
    public void User_permissions_map_through_the_name_directory()
    {
        var principals = GoogleDriveMapping.MapPermissions(
            [("user", "alice@corp.com"), ("user", "unknown@corp.com"), ("group", "hr-team@corp.com")],
            "gdrive",
            email => email == "alice@corp.com" ? "alice" : email == "hr-team@corp.com" ? "hr" : null);

        Assert.Equal(["user:alice", "user:unknown@corp.com", "group:hr"], principals);
    }

    [Fact]
    public void Domain_or_public_sharing_becomes_tenant_wide()
    {
        var principals = GoogleDriveMapping.MapPermissions(
            [("user", "alice@corp.com"), ("domain", null)], "gdrive", _ => null);

        Assert.Equal(["tenant:gdrive:all"], principals);
    }

    [Fact]
    public void Unreadable_acl_defaults_to_tenant_wide()
    {
        var principals = GoogleDriveMapping.MapPermissions([], "gdrive", _ => null);

        Assert.Equal(["tenant:gdrive:all"], principals);
    }
}