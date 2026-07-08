using System.Text.RegularExpressions;

namespace KnowVault.Domain.Chunking;

/// <summary>
/// Structure-aware chunking: split on the document's own sections first,
/// then recursively (paragraphs → sentences → words) pack each section into
/// token-budgeted chunks with overlap. Breadcrumbs ("Title &gt; Section") are
/// prepended to the embedded text — cheap, and a large retrieval win.
/// </summary>
public sealed partial class DocumentChunker(ITokenCounter tokenCounter)
{
    public IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document, ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var chunks = new List<DocumentChunk>();

        foreach (var section in GroupIntoSections(document.Blocks))
        {
            var breadcrumb = BuildBreadcrumb(document.Title, section.HeadingPath);
            var breadcrumbTokens = options.IncludeBreadcrumbs ? tokenCounter.Count(breadcrumb) : 0;

            // The breadcrumb spends part of every chunk's budget; keep a floor
            // so a pathological heading trail can't squeeze content out entirely.
            var budget = Math.Max(ChunkingOptions.MinTokensPerChunk, options.TargetTokensPerChunk - breadcrumbTokens);

            var pieces = SplitIntoPieces(section, options, budget);
            PackPiecesIntoChunks(pieces, budget, options, breadcrumb, chunks);
        }

        return chunks;
    }

    private sealed record Section(IReadOnlyList<string> HeadingPath, List<DocumentBlock> Blocks);

    private sealed record Piece(string Text, int Tokens);

    private static IEnumerable<Section> GroupIntoSections(IReadOnlyList<DocumentBlock> blocks)
    {
        Section? current = null;

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
            {
                continue;
            }

            if (current is null || !current.HeadingPath.SequenceEqual(block.HeadingPath, StringComparer.Ordinal))
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = new Section(block.HeadingPath, []);
            }

            current.Blocks.Add(block);
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    private static string BuildBreadcrumb(string title, IReadOnlyList<string> headingPath)
    {
        var parts = new List<string>(headingPath.Count + 1);
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title.Trim());
        }

        parts.AddRange(headingPath.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()));
        return string.Join(" > ", parts);
    }

    /// <summary>
    /// Break a section into pieces that each fit the budget: paragraphs first,
    /// oversized paragraphs into sentences, oversized sentences into word runs.
    /// Tables stay whole when they fit (and are split by rows when they don't).
    /// </summary>
    private List<Piece> SplitIntoPieces(Section section, ChunkingOptions options, int budget)
    {
        var pieces = new List<Piece>();

        foreach (var block in section.Blocks)
        {
            var text = block.Text.Trim();
            var tokens = tokenCounter.Count(text);

            if (block.IsTable && options.KeepTablesWhole && tokens <= budget)
            {
                pieces.Add(new Piece(text, tokens));
                continue;
            }

            if (block.IsTable && tokens > budget)
            {
                // Split oversized tables on row boundaries, never mid-row.
                AddSplit(text, budget, pieces, SplitLines);
                continue;
            }

            AddSplit(text, budget, pieces, SplitParagraphs);
        }

        return pieces;
    }

    private void AddSplit(string text, int budget, List<Piece> pieces, Func<string, IEnumerable<string>> splitter)
    {
        foreach (var part in splitter(text))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var tokens = tokenCounter.Count(trimmed);
            if (tokens <= budget)
            {
                pieces.Add(new Piece(trimmed, tokens));
            }
            else if (splitter == SplitParagraphs)
            {
                AddSplit(trimmed, budget, pieces, SplitSentences);
            }
            else
            {
                AddWordRuns(trimmed, budget, pieces);
            }
        }
    }

    private void AddWordRuns(string text, int budget, List<Piece> pieces)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var run = new List<string>();
        var runTokens = 0;

        foreach (var word in words)
        {
            // Per-word counts summed are a close, always-safe over-estimate of
            // the joined run's count, and keep this O(n) in tokenizer calls.
            var wordTokens = tokenCounter.Count(word);

            if (run.Count > 0 && runTokens + wordTokens > budget)
            {
                pieces.Add(new Piece(string.Join(' ', run), runTokens));
                run.Clear();
                runTokens = 0;
            }

            run.Add(word);
            runTokens += wordTokens;
        }

        if (run.Count > 0)
        {
            pieces.Add(new Piece(string.Join(' ', run), runTokens));
        }
    }

    private void PackPiecesIntoChunks(
        List<Piece> pieces, int budget, ChunkingOptions options, string breadcrumb, List<DocumentChunk> chunks)
    {
        var current = new List<Piece>();
        var currentTokens = 0;
        Piece? overlap = null;

        foreach (var piece in pieces)
        {
            if (current.Count > 0 && currentTokens + piece.Tokens > budget)
            {
                Flush();
            }

            current.Add(piece);
            currentTokens += piece.Tokens;
        }

        Flush();

        void Flush()
        {
            if (current.Count == 0 || (current.Count == 1 && ReferenceEquals(current[0], overlap)))
            {
                return;
            }

            var content = string.Join("\n\n", current.Select(p => p.Text));
            var embeddedText = options.IncludeBreadcrumbs && breadcrumb.Length > 0
                ? $"{breadcrumb}\n\n{content}"
                : content;

            chunks.Add(new DocumentChunk(
                chunks.Count,
                embeddedText,
                content,
                breadcrumb,
                tokenCounter.Count(embeddedText)));

            overlap = options.OverlapTokens > 0 ? TakeTail(content, options.OverlapTokens) : null;
            current.Clear();
            currentTokens = 0;

            if (overlap is not null)
            {
                current.Add(overlap);
                currentTokens = overlap.Tokens;
            }
        }
    }

    /// <summary>Trailing sentences of a chunk, up to roughly the overlap budget.</summary>
    private Piece? TakeTail(string content, int overlapTokens)
    {
        var sentences = SplitSentences(content).ToList();
        var tail = new List<string>();
        var tokens = 0;

        for (var i = sentences.Count - 1; i >= 0; i--)
        {
            var sentenceTokens = tokenCounter.Count(sentences[i]);
            if (tail.Count > 0 && tokens + sentenceTokens > overlapTokens)
            {
                break;
            }

            tail.Insert(0, sentences[i]);
            tokens += sentenceTokens;
        }

        // Overlap that swallows the whole previous chunk adds no signal.
        if (tail.Count == sentences.Count)
        {
            return null;
        }

        return tail.Count == 0 ? null : new Piece(string.Join(" ", tail), tokens);
    }

    private static IEnumerable<string> SplitParagraphs(string text) => ParagraphBreak().Split(text);

    private static IEnumerable<string> SplitSentences(string text) => SentenceBreak().Split(text);

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();
}