using System.Text;
using System.Text.RegularExpressions;

using KnowVault.Domain.Chunking;

namespace KnowVault.Domain.Extraction;

/// <summary>
/// Structure-aware markdown parsing: ATX headings become the heading path,
/// pipe tables are flagged so the chunker keeps them whole, fenced code blocks
/// are preserved verbatim. A leading H1 becomes the document title.
/// </summary>
public sealed partial class MarkdownParser : IDocumentParser
{
    public ExtractedDocument Parse(string content, string fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<DocumentBlock>();
        var headingStack = new List<(int Level, string Text)>();
        var title = fallbackTitle;
        var titleTaken = false;
        var paragraph = new StringBuilder();
        var inCodeFence = false;

        void FlushParagraph(bool isTable = false)
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            var text = paragraph.ToString().Trim();
            paragraph.Clear();
            if (text.Length > 0)
            {
                blocks.Add(new DocumentBlock([.. headingStack.Select(h => h.Text)], text, isTable));
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (CodeFence().IsMatch(line))
            {
                inCodeFence = !inCodeFence;
                paragraph.AppendLine(line);
                continue;
            }

            if (inCodeFence)
            {
                paragraph.AppendLine(line);
                continue;
            }

            var heading = AtxHeading().Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                var level = heading.Groups[1].Length;
                var text = heading.Groups[2].Value.Trim();

                if (level == 1 && !titleTaken && blocks.Count == 0 && headingStack.Count == 0)
                {
                    title = text;
                    titleTaken = true;
                    continue;
                }

                headingStack.RemoveAll(h => h.Level >= level);
                headingStack.Add((level, text));
                continue;
            }

            if (IsTableLine(line))
            {
                FlushParagraph();
                paragraph.AppendLine(line.Trim());
                while (i + 1 < lines.Length && IsTableLine(lines[i + 1]))
                {
                    paragraph.AppendLine(lines[++i].Trim());
                }

                FlushParagraph(isTable: true);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraph.Length > 0)
                {
                    paragraph.AppendLine();
                }

                continue;
            }

            paragraph.AppendLine(line);
        }

        FlushParagraph();
        return new ExtractedDocument(title, blocks);
    }

    private static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^\s*(```|~~~)")]
    private static partial Regex CodeFence();
}