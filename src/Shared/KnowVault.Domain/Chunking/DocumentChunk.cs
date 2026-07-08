namespace KnowVault.Domain.Chunking;

/// <summary>
/// A chunk ready for embedding and indexing.
/// </summary>
/// <param name="Index">Zero-based position within the document; neighbor expansion uses Index ± 1.</param>
/// <param name="EmbeddedText">What gets embedded and indexed: breadcrumb prefix + content.</param>
/// <param name="Content">The content without the breadcrumb prefix.</param>
/// <param name="Breadcrumb">"Title &gt; Section &gt; Subsection" trail this chunk came from.</param>
/// <param name="TokenCount">Token count of <paramref name="EmbeddedText"/>.</param>
public sealed record DocumentChunk(
    int Index,
    string EmbeddedText,
    string Content,
    string Breadcrumb,
    int TokenCount);