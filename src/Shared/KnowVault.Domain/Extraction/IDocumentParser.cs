using KnowVault.Domain.Chunking;

namespace KnowVault.Domain.Extraction;

/// <summary>
/// Turns raw text formats into the parser-agnostic <see cref="ExtractedDocument"/>
/// shape the chunker consumes. Pure-text parsers (markdown, plain text) live in
/// Domain; parsers needing external services (Document Intelligence for PDF)
/// implement the same output shape inside the Ingestion service.
/// </summary>
public interface IDocumentParser
{
    /// <param name="content">Raw file content.</param>
    /// <param name="fallbackTitle">Used when the document declares no title of its own (typically the file name).</param>
    ExtractedDocument Parse(string content, string fallbackTitle);
}