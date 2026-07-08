namespace KnowVault.Domain.Chunking;

/// <summary>
/// Parser-agnostic representation of an extracted document: an ordered list
/// of blocks, each carrying the heading path it appeared under. Extractors
/// (Markdown, HTML, Document Intelligence) all produce this shape.
/// </summary>
public sealed record ExtractedDocument(string Title, IReadOnlyList<DocumentBlock> Blocks);

/// <summary>One contiguous block of content under a heading path.</summary>
/// <param name="HeadingPath">Headings from outermost to innermost, excluding the document title.</param>
/// <param name="Text">Block text; tables are pre-rendered as markdown.</param>
/// <param name="IsTable">Tables are kept whole where possible instead of being split mid-row.</param>
public sealed record DocumentBlock(IReadOnlyList<string> HeadingPath, string Text, bool IsTable = false);