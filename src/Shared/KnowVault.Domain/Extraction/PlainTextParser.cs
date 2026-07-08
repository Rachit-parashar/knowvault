using KnowVault.Domain.Chunking;

namespace KnowVault.Domain.Extraction;

/// <summary>Plain text: paragraphs separated by blank lines, no structure.</summary>
public sealed class PlainTextParser : IDocumentParser
{
    public ExtractedDocument Parse(string content, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ExtractedDocument(fallbackTitle, [new DocumentBlock([], content)]);
    }
}