using System.Text;
using System.Text.RegularExpressions;

namespace HolidayLights.App.Help;

/// <summary>
/// Parses the Markdown subset of the help topics (<c>HelpContent/README.md</c>): headings, paragraphs, bulleted and
/// numbered lists, <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, tables, pictures on their own line, links to other
/// topics (<c>topic:&lt;id&gt;</c>) and page buttons (<c>settings:&lt;page&gt;</c> links alone on a line). Pure.
/// </summary>
public static partial class MarkdownParser
{
    /// <summary>Parses a topic.</summary>
    /// <param name="markdown">The Markdown text.</param>
    /// <returns>The document.</returns>
    public static MarkdownDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<MarkdownBlock>();
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
            }
            else if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                blocks.Add(new HeadingBlock(heading.Groups[1].Length, ParseInlines(heading.Groups[2].Value.Trim())));
                i++;
            }
            else if (ImagePattern().Match(line.Trim()) is { Success: true } image)
            {
                blocks.Add(new ImageBlock(image.Groups[2].Value, image.Groups[1].Value));
                i++;
            }
            else if (IsTableStart(lines, i))
            {
                blocks.Add(ParseTable(lines, ref i));
            }
            else if (ListItemPattern().IsMatch(line))
            {
                blocks.Add(ParseList(lines, ref i));
            }
            else
            {
                blocks.Add(ParseParagraph(lines, ref i));
            }
        }

        return new MarkdownDocument(blocks);
    }

    /// <summary>Parses the runs of one block of text.</summary>
    /// <param name="text">The text (one logical line).</param>
    /// <returns>The runs; adjacent runs of the same style are merged.</returns>
    public static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var runs = new List<MarkdownInline>();
        var pending = new StringBuilder();
        bool bold = false;
        bool italic = false;

        void Flush()
        {
            if (pending.Length > 0)
            {
                Add(runs, new MarkdownInline(pending.ToString(), StyleOf(bold, italic)));
                pending.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '`' && text.IndexOf('`', i + 1) is int close and > 0)
            {
                Flush();
                Add(runs, new MarkdownInline(text[(i + 1)..close], InlineStyle.Code));
                i = close;
            }
            else if (c == '[' && LinkPattern().Match(text, i) is { Success: true } link && link.Index == i)
            {
                Flush();
                Add(runs, new MarkdownInline(link.Groups[1].Value, StyleOf(bold, italic), link.Groups[2].Value));
                i += link.Length - 1;
            }
            else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
            }
            else if (c == '*' && (italic || IsEmphasisOpener(text, i)))
            {
                Flush();
                italic = !italic;
            }
            else
            {
                pending.Append(c);
            }
        }

        Flush();
        return runs;
    }

    private static bool IsEmphasisOpener(string text, int index) =>
        index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1]) && text.IndexOf('*', index + 1) > index;

    private static InlineStyle StyleOf(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => InlineStyle.BoldItalic,
        (true, false) => InlineStyle.Bold,
        (false, true) => InlineStyle.Italic,
        _ => InlineStyle.Normal,
    };

    private static void Add(List<MarkdownInline> runs, MarkdownInline run)
    {
        if (runs.Count > 0 && runs[^1] is { LinkTarget: null } last && run.LinkTarget is null && last.Style == run.Style)
        {
            runs[^1] = last with { Text = last.Text + run.Text };
        }
        else
        {
            runs.Add(run);
        }
    }

    private static MarkdownBlock ParseParagraph(string[] lines, ref int i)
    {
        var text = new List<string>();
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines, i))
        {
            text.Add(lines[i].Trim());
            i++;
        }

        string joined = string.Join(' ', text);
        if (PageButtonPattern().Match(joined) is { Success: true } button)
        {
            return new PageButtonBlock(button.Groups[1].Value, button.Groups[2].Value);
        }

        return new ParagraphBlock(ParseInlines(joined));
    }

    private static ListBlock ParseList(string[] lines, ref int i)
    {
        bool ordered = char.IsDigit(lines[i].TrimStart()[0]);
        var items = new List<IReadOnlyList<MarkdownInline>>();
        StringBuilder? item = null;
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
        {
            string line = lines[i];
            if (ListItemPattern().Match(line) is { Success: true } marker)
            {
                if (item is not null)
                {
                    items.Add(ParseInlines(item.ToString()));
                }

                item = new StringBuilder(line[marker.Length..].Trim());
            }
            else if (StartsBlock(lines, i))
            {
                break;
            }
            else
            {
                item!.Append(' ').Append(line.Trim());
            }

            i++;
        }

        if (item is not null)
        {
            items.Add(ParseInlines(item.ToString()));
        }

        return new ListBlock(ordered, items);
    }

    private static TableBlock ParseTable(string[] lines, ref int i)
    {
        IReadOnlyList<IReadOnlyList<MarkdownInline>> header = ParseRow(lines[i]);
        i += 2;
        var rows = new List<IReadOnlyList<IReadOnlyList<MarkdownInline>>>();
        while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
        {
            rows.Add(ParseRow(lines[i]));
            i++;
        }

        return new TableBlock(header, rows);
    }

    private static IReadOnlyList<IReadOnlyList<MarkdownInline>> ParseRow(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        var cells = new List<IReadOnlyList<MarkdownInline>>();
        var cell = new StringBuilder();
        bool inCode = false;
        for (int c = 0; c < trimmed.Length; c++)
        {
            char ch = trimmed[c];
            if (ch == '\\' && c + 1 < trimmed.Length && trimmed[c + 1] == '|')
            {
                cell.Append('|');
                c++;
            }
            else if (ch == '|' && !inCode)
            {
                cells.Add(ParseInlines(cell.ToString().Trim()));
                cell.Clear();
            }
            else
            {
                inCode ^= ch == '`';
                cell.Append(ch);
            }
        }

        cells.Add(ParseInlines(cell.ToString().Trim()));
        return cells;
    }

    private static bool StartsBlock(string[] lines, int i) =>
        HeadingPattern().IsMatch(lines[i]) || ListItemPattern().IsMatch(lines[i]) || IsTableStart(lines, i)
        || ImagePattern().IsMatch(lines[i].Trim());

    private static bool IsTableStart(string[] lines, int i) =>
        lines[i].TrimStart().StartsWith('|') && i + 1 < lines.Length && TableDelimiterPattern().IsMatch(lines[i + 1]);

    [GeneratedRegex(@"^(#{1,3})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)\s]+)\)$")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"^\s*([-*]|\d+\.)\s+")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$")]
    private static partial Regex TableDelimiterPattern();

    [GeneratedRegex(@"\G\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"^\[([^\]]+)\]\((settings:[a-z]+)\)$")]
    private static partial Regex PageButtonPattern();
}
