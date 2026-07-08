using KnowVault.Domain.Extraction;

namespace KnowVault.Domain.Tests.Extraction;

public class MarkdownParserTests
{
    private readonly MarkdownParser _parser = new();

    [Fact]
    public void Leading_h1_becomes_title_and_is_excluded_from_heading_path()
    {
        var doc = _parser.Parse("# Getting Started\n\nSome intro text.", "file.md");

        Assert.Equal("Getting Started", doc.Title);
        var block = Assert.Single(doc.Blocks);
        Assert.Empty(block.HeadingPath);
        Assert.Equal("Some intro text.", block.Text);
    }

    [Fact]
    public void Fallback_title_used_when_no_h1()
    {
        var doc = _parser.Parse("Just some text.", "notes.md");

        Assert.Equal("notes.md", doc.Title);
    }

    [Fact]
    public void Nested_headings_build_the_path()
    {
        var markdown = """
            # Title
            ## Setup
            ### Prerequisites
            You need a subscription.
            ## Usage
            Run the command.
            """;

        var doc = _parser.Parse(markdown, "f.md");

        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal(["Setup", "Prerequisites"], doc.Blocks[0].HeadingPath);
        Assert.Equal("You need a subscription.", doc.Blocks[0].Text);
        Assert.Equal(["Usage"], doc.Blocks[1].HeadingPath);
    }

    [Fact]
    public void Sibling_heading_replaces_previous_at_same_level()
    {
        var markdown = "## A\n### A1\ntext a1\n### A2\ntext a2";

        var doc = _parser.Parse(markdown, "f.md");

        Assert.Equal(["A", "A1"], doc.Blocks[0].HeadingPath);
        Assert.Equal(["A", "A2"], doc.Blocks[1].HeadingPath);
    }

    [Fact]
    public void Pipe_tables_are_flagged_as_tables()
    {
        var markdown = """
            ## Pricing
            Intro paragraph.

            | Tier | Cost |
            |------|------|
            | Basic | $10 |

            Closing paragraph.
            """;

        var doc = _parser.Parse(markdown, "f.md");

        Assert.Equal(3, doc.Blocks.Count);
        Assert.False(doc.Blocks[0].IsTable);
        Assert.True(doc.Blocks[1].IsTable);
        Assert.Contains("| Basic | $10 |", doc.Blocks[1].Text, StringComparison.Ordinal);
        Assert.False(doc.Blocks[2].IsTable);
    }

    [Fact]
    public void Code_fences_are_preserved_and_headings_inside_are_not_parsed()
    {
        var markdown = """
            ## Example
            ```bash
            # this is a comment, not a heading
            echo hi
            ```
            """;

        var doc = _parser.Parse(markdown, "f.md");

        var block = Assert.Single(doc.Blocks);
        Assert.Equal(["Example"], block.HeadingPath);
        Assert.Contains("# this is a comment, not a heading", block.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Blank_lines_separate_paragraphs_within_a_section()
    {
        var doc = _parser.Parse("## S\npara one line one\npara one line two\n\npara two", "f.md");

        var block = Assert.Single(doc.Blocks);
        Assert.Contains("para one line one\npara one line two", block.Text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("para two", block.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_h1_later_in_document_is_a_heading_not_a_title()
    {
        var doc = _parser.Parse("# Real Title\ntext\n# Another Top Section\nmore", "f.md");

        Assert.Equal("Real Title", doc.Title);
        Assert.Equal(2, doc.Blocks.Count);
        Assert.Empty(doc.Blocks[0].HeadingPath);
        Assert.Equal(["Another Top Section"], doc.Blocks[1].HeadingPath);
    }

    [Fact]
    public void Empty_document_produces_no_blocks()
    {
        var doc = _parser.Parse("", "empty.md");

        Assert.Empty(doc.Blocks);
        Assert.Equal("empty.md", doc.Title);
    }
}

public class PlainTextParserTests
{
    [Fact]
    public void Wraps_content_in_single_unstructured_block()
    {
        var doc = new PlainTextParser().Parse("line one\n\nline two", "notes.txt");

        Assert.Equal("notes.txt", doc.Title);
        var block = Assert.Single(doc.Blocks);
        Assert.Empty(block.HeadingPath);
        Assert.False(block.IsTable);
    }
}