using KnowVault.Domain.Chunking;

namespace KnowVault.Domain.Tests.Chunking;

public class DocumentChunkerTests
{
    /// <summary>Deterministic counter for tests: one token per whitespace-separated word.</summary>
    private sealed class WordTokenCounter : ITokenCounter
    {
        public int Count(string text) =>
            string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static readonly ChunkingOptions SmallChunks = new() { TargetTokensPerChunk = 64 };

    private readonly DocumentChunker _chunker = new(new WordTokenCounter());

    private static string Words(int count, string prefix = "w") =>
        string.Join(' ', Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

    [Fact]
    public void Small_document_becomes_one_chunk_with_breadcrumb()
    {
        var doc = new ExtractedDocument("My Doc", [new DocumentBlock(["Intro"], "Short content here.")]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Equal("My Doc > Intro", chunk.Breadcrumb);
        Assert.Equal("Short content here.", chunk.Content);
        Assert.StartsWith("My Doc > Intro", chunk.EmbeddedText, StringComparison.Ordinal);
        Assert.Contains("Short content here.", chunk.EmbeddedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Breadcrumbs_can_be_disabled()
    {
        var doc = new ExtractedDocument("My Doc", [new DocumentBlock(["Intro"], "Short content here.")]);

        var chunks = _chunker.Chunk(doc, SmallChunks with { IncludeBreadcrumbs = false });

        Assert.Equal("Short content here.", Assert.Single(chunks).EmbeddedText);
    }

    [Fact]
    public void Sections_are_never_merged_across_heading_boundaries()
    {
        var doc = new ExtractedDocument("Doc",
        [
            new DocumentBlock(["Alpha"], "Alpha content."),
            new DocumentBlock(["Beta"], "Beta content."),
        ]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Doc > Alpha", chunks[0].Breadcrumb);
        Assert.Equal("Doc > Beta", chunks[1].Breadcrumb);
        Assert.DoesNotContain("Beta content", chunks[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_section_splits_into_budgeted_chunks()
    {
        // 10 paragraphs x 20 words with a 64-token budget → must split.
        var paragraphs = string.Join("\n\n", Enumerable.Range(1, 10).Select(p => Words(20, $"p{p}w") + "."));
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], paragraphs)]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        Assert.True(chunks.Count >= 3, $"expected at least 3 chunks, got {chunks.Count}");
        foreach (var chunk in chunks)
        {
            var contentTokens = new WordTokenCounter().Count(chunk.Content);
            Assert.True(contentTokens <= 64, $"chunk {chunk.Index} has {contentTokens} tokens");
        }
    }

    [Fact]
    public void Consecutive_chunks_overlap()
    {
        var paragraphs = string.Join("\n\n", Enumerable.Range(1, 10).Select(p => Words(20, $"p{p}w") + "."));
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], paragraphs)]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        for (var i = 1; i < chunks.Count; i++)
        {
            // Each chunk starts with the tail sentence(s) of its predecessor.
            var previousTailWord = chunks[i - 1].Content
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1];
            Assert.Contains(previousTailWord, chunks[i].Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Overlap_can_be_disabled()
    {
        var paragraphs = string.Join("\n\n", Enumerable.Range(1, 6).Select(p => Words(30, $"p{p}w") + "."));
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], paragraphs)]);

        var chunks = _chunker.Chunk(doc, SmallChunks with { OverlapRatio = 0 });

        var allWords = chunks.SelectMany(c => c.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var duplicates = allWords.GroupBy(w => w, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Table_that_fits_stays_whole()
    {
        var table = string.Join('\n', Enumerable.Range(1, 8).Select(r => $"| row{r}a | row{r}b |"));
        var doc = new ExtractedDocument("Doc",
        [
            new DocumentBlock([], Words(50) + "."),
            new DocumentBlock([], table, IsTable: true),
        ]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        // The full table appears intact in exactly one chunk.
        Assert.Single(chunks, c => c.Content.Contains(table, StringComparison.Ordinal));
    }

    [Fact]
    public void Oversized_table_splits_on_row_boundaries()
    {
        var rows = Enumerable.Range(1, 40).Select(r => $"| row{r}col1 | row{r}col2 | row{r}col3 |").ToList();
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], string.Join('\n', rows), IsTable: true)]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        Assert.True(chunks.Count > 1);
        var rowSet = rows.ToHashSet(StringComparer.Ordinal);
        foreach (var line in chunks.SelectMany(c => c.Content.Split("\n\n")).SelectMany(s => s.Split('\n')))
        {
            Assert.Contains(line, rowSet);
        }
    }

    [Fact]
    public void Oversized_sentence_is_hard_split_by_words()
    {
        // One 300-word "sentence" with no punctuation — worst case input.
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], Words(300))]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        Assert.True(chunks.Count >= 4);
        var counter = new WordTokenCounter();
        Assert.All(chunks, c => Assert.True(counter.Count(c.Content) <= 64));
    }

    [Fact]
    public void Chunk_indexes_are_sequential_across_sections()
    {
        var doc = new ExtractedDocument("Doc",
        [
            new DocumentBlock(["A"], Words(100) + "."),
            new DocumentBlock(["B"], Words(100) + "."),
        ]);

        var chunks = _chunker.Chunk(doc, SmallChunks);

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
    }

    [Fact]
    public void Whitespace_only_document_produces_no_chunks()
    {
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], "   \n\n  ")]);

        Assert.Empty(_chunker.Chunk(doc, SmallChunks));
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        var doc = new ExtractedDocument("Doc", [new DocumentBlock([], "text")]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _chunker.Chunk(doc, new ChunkingOptions { TargetTokensPerChunk = 10 }));
    }
}

public class Cl100kTokenCounterTests
{
    [Fact]
    public void Counts_real_tokens()
    {
        var counter = new Cl100kTokenCounter();

        Assert.Equal(0, counter.Count(""));
        Assert.True(counter.Count("hello world") is >= 2 and <= 3);
    }

    [Fact]
    public void End_to_end_with_real_tokenizer_respects_budget()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 30).Select(p =>
            $"Paragraph {p}: Azure AI Search supports hybrid retrieval combining BM25 keyword scoring " +
            "with vector similarity, merged via reciprocal rank fusion for better relevance."));
        var doc = new ExtractedDocument("Hybrid Search Guide", [new DocumentBlock(["Retrieval"], text)]);

        var chunker = new DocumentChunker(new Cl100kTokenCounter());
        var chunks = chunker.Chunk(doc, new ChunkingOptions());

        Assert.True(chunks.Count > 1);
        // Joiner tokens ("\n\n") make embedded counts run slightly past the
        // per-piece budget sum; 600 gives headroom over the 512 target.
        Assert.All(chunks, c => Assert.True(c.TokenCount <= 600, $"chunk {c.Index}: {c.TokenCount} tokens"));
        Assert.All(chunks, c => Assert.StartsWith("Hybrid Search Guide > Retrieval", c.EmbeddedText, StringComparison.Ordinal));
    }
}