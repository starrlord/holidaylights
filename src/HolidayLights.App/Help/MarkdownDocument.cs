namespace HolidayLights.App.Help;

/// <summary>How a run of text is drawn.</summary>
public enum InlineStyle
{
    /// <summary>Plain text.</summary>
    Normal,

    /// <summary><c>**bold**</c> (UI labels in the topics).</summary>
    Bold,

    /// <summary><c>*italic*</c>.</summary>
    Italic,

    /// <summary>Bold and italic.</summary>
    BoldItalic,

    /// <summary><c>`code`</c> (folder names, characters).</summary>
    Code,
}

/// <summary>A run of text inside a block: plain, emphasized, code, or a link to another topic.</summary>
/// <param name="Text">The text.</param>
/// <param name="Style">How it is drawn.</param>
/// <param name="LinkTarget">For links: <c>topic:&lt;id&gt;</c> or <c>settings:&lt;page&gt;</c>; null otherwise.</param>
public sealed record MarkdownInline(string Text, InlineStyle Style = InlineStyle.Normal, string? LinkTarget = null);

/// <summary>A block of a topic.</summary>
public abstract record MarkdownBlock;

/// <summary>A heading (<c>#</c> to <c>###</c>).</summary>
/// <param name="Level">1 to 3.</param>
/// <param name="Inlines">Its text.</param>
public sealed record HeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A paragraph.</summary>
/// <param name="Inlines">Its text.</param>
public sealed record ParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A bulleted (<c>-</c>, <c>*</c>) or numbered (<c>1.</c>) list.</summary>
/// <param name="Ordered">True for a numbered list.</param>
/// <param name="Items">The items.</param>
public sealed record ListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<MarkdownInline>> Items) : MarkdownBlock;

/// <summary>A table with a header row.</summary>
/// <param name="Header">The header cells.</param>
/// <param name="Rows">The rows of cells.</param>
public sealed record TableBlock(IReadOnlyList<IReadOnlyList<MarkdownInline>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInline>>> Rows) : MarkdownBlock;

/// <summary>A picture on a line of its own (<c>![alt](images/name.png)</c>).</summary>
/// <param name="Source">The path relative to the help content (<c>images/name.png</c>).</param>
/// <param name="AltText">The description read by screen readers.</param>
public sealed record ImageBlock(string Source, string AltText) : MarkdownBlock;

/// <summary>A page button: a link alone on its line ("Open the Bulb Factory page").</summary>
/// <param name="Text">The button text.</param>
/// <param name="Target">The link target, e.g. <c>settings:bulbs</c>.</param>
public sealed record PageButtonBlock(string Text, string Target) : MarkdownBlock;

/// <summary>A parsed help topic.</summary>
/// <param name="Blocks">The blocks in order.</param>
public sealed record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks)
{
    /// <summary>The text of the first level-1 heading (the topic title), or empty.</summary>
    public string Title => Blocks.OfType<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { } h ? PlainText(h.Inlines) : "";

    /// <summary>All the text of the topic without formatting (search).</summary>
    /// <returns>The words of every block, separated by spaces.</returns>
    public string ToPlainText() => string.Join(' ', Blocks.Select(PlainText));

    /// <summary>The text of a run list without formatting.</summary>
    /// <param name="inlines">The runs.</param>
    /// <returns>The concatenated text.</returns>
    public static string PlainText(IEnumerable<MarkdownInline> inlines) => string.Concat(inlines.Select(i => i.Text));

    private static string PlainText(MarkdownBlock block) => block switch
    {
        HeadingBlock heading => PlainText(heading.Inlines),
        ParagraphBlock paragraph => PlainText(paragraph.Inlines),
        ListBlock list => string.Join(' ', list.Items.Select(PlainText)),
        TableBlock table => string.Join(' ', table.Header.Concat(table.Rows.SelectMany(r => r)).Select(PlainText)),
        ImageBlock image => image.AltText,
        PageButtonBlock button => button.Text,
        _ => "",
    };
}
