using Azure;
using Azure.AI.DocumentIntelligence;

namespace KnowVault.Ingestion.Pipeline;

/// <summary>Turns binary documents (PDFs, scans) into markdown for the standard parsing path.</summary>
public interface IPdfExtractor
{
    Task<string> ExtractMarkdownAsync(BinaryData content, CancellationToken cancellationToken);
}

/// <summary>
/// Azure Document Intelligence prebuilt-layout with markdown output: headings
/// become ATX headings and tables become pipe tables, so PDF content flows
/// through the exact same MarkdownParser → chunker path as native markdown.
/// </summary>
public sealed class DocumentIntelligencePdfExtractor(DocumentIntelligenceClient client) : IPdfExtractor
{
    public async Task<string> ExtractMarkdownAsync(BinaryData content, CancellationToken cancellationToken)
    {
        var options = new AnalyzeDocumentOptions("prebuilt-layout", content)
        {
            OutputContentFormat = DocumentContentFormat.Markdown,
        };

        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
        return operation.Value.Content;
    }
}

/// <summary>Used when no Document Intelligence endpoint is configured: PDFs fail fast to the DLQ.</summary>
public sealed class UnavailablePdfExtractor : IPdfExtractor
{
    public Task<string> ExtractMarkdownAsync(BinaryData content, CancellationToken cancellationToken) =>
        throw new NotSupportedException("PDF extraction requires a configured Document Intelligence endpoint.");
}