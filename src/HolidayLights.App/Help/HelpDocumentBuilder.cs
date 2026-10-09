using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HolidayLights.App.Help;

/// <summary>
/// Renders a parsed help topic as a WPF <see cref="FlowDocument"/> (PRODUCT-SPEC 3.10): the 2003 help banner on top, the
/// headings, paragraphs, lists, tables and pictures (at their natural size), links to other topics and the "Open the
/// &lt;page&gt; page" buttons. Text follows the Fluent theme's colours and stays selectable. UI thread.
/// </summary>
public sealed class HelpDocumentBuilder
{
    private const string TopicScheme = "topic:";
    private const string SettingsScheme = "settings:";
    private static readonly FontFamily BodyFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    private readonly Action<string> openTopic;
    private readonly Action<SettingsPageId> openPage;
    private readonly Func<string, ImageSource?> loadImage;

    /// <summary>Creates the builder.</summary>
    /// <param name="openTopic">Opens another topic (a <c>topic:</c> link).</param>
    /// <param name="openPage">Opens Settings on a page (a <c>settings:</c> button).</param>
    /// <param name="loadImage">Loads a picture of the help content (<c>images/...</c>), or returns null when it is missing.</param>
    public HelpDocumentBuilder(Action<string> openTopic, Action<SettingsPageId> openPage, Func<string, ImageSource?> loadImage)
    {
        ArgumentNullException.ThrowIfNull(openTopic);
        ArgumentNullException.ThrowIfNull(openPage);
        ArgumentNullException.ThrowIfNull(loadImage);
        this.openTopic = openTopic;
        this.openPage = openPage;
        this.loadImage = loadImage;
    }

    /// <summary>Loads a picture embedded in the help content at its own DPI (192 DPI pictures show at half their pixel size).</summary>
    /// <param name="path">The path relative to <c>HelpContent</c>.</param>
    /// <returns>The frozen picture, or null when it is not embedded.</returns>
    public static ImageSource? LoadEmbeddedImage(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!HelpContentStore.Exists(path))
        {
            return null;
        }

        using Stream stream = HelpContentStore.Open(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Builds the document of a topic.</summary>
    /// <param name="topic">The parsed topic.</param>
    /// <param name="banner">The help banner shown above every topic, or null.</param>
    /// <returns>The document.</returns>
    public FlowDocument Build(MarkdownDocument topic, ImageSource? banner)
    {
        ArgumentNullException.ThrowIfNull(topic);
        var document = new FlowDocument
        {
            FontFamily = BodyFont,
            FontSize = 14,
            PagePadding = new Thickness(24, 16, 24, 24),
            TextAlignment = TextAlignment.Left,
            Background = Brushes.Transparent,
            IsHyphenationEnabled = false,
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextFillColorPrimaryBrush");
        if (banner is not null)
        {
            var bannerImage = new Image { Source = banner, Width = 216, Height = 53, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(bannerImage, BitmapScalingMode.Fant);
            AutomationProperties.SetName(bannerImage, "Holiday Lights");
            document.Blocks.Add(new BlockUIContainer(bannerImage) { Margin = new Thickness(0, 0, 0, 12) });
        }

        foreach (MarkdownBlock block in topic.Blocks)
        {
            document.Blocks.Add(Create(block));
        }

        return document;
    }

    private Block Create(MarkdownBlock block) => block switch
    {
        HeadingBlock heading => Heading(heading),
        ParagraphBlock paragraph => Paragraph(paragraph.Inlines),
        ListBlock list => List(list),
        TableBlock table => Table(table),
        ImageBlock image => Picture(image),
        PageButtonBlock button => PageButton(button),
        _ => new Paragraph(),
    };

    private Paragraph Heading(HeadingBlock heading)
    {
        Paragraph paragraph = Paragraph(heading.Inlines);
        paragraph.FontWeight = FontWeights.SemiBold;
        (paragraph.FontSize, paragraph.Margin) = heading.Level switch
        {
            1 => (28d, new Thickness(0, 0, 0, 12)),
            2 => (20d, new Thickness(0, 16, 0, 8)),
            _ => (16d, new Thickness(0, 12, 0, 6)),
        };
        return paragraph;
    }

    private Paragraph Paragraph(IReadOnlyList<MarkdownInline> inlines)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        foreach (MarkdownInline inline in inlines)
        {
            paragraph.Inlines.Add(Inline(inline));
        }

        return paragraph;
    }

    private Inline Inline(MarkdownInline inline)
    {
        if (inline.LinkTarget is { } target)
        {
            var link = new Hyperlink(new Run(inline.Text));
            link.Click += (_, _) => Follow(target);
            AutomationProperties.SetHelpText(link, target.StartsWith(SettingsScheme, StringComparison.Ordinal) ? "Opens Holiday Lights Settings" : "Opens a help topic");
            return link;
        }

        return inline.Style switch
        {
            InlineStyle.Bold => new Bold(new Run(inline.Text)),
            InlineStyle.Italic => new Italic(new Run(inline.Text)),
            InlineStyle.BoldItalic => new Bold(new Italic(new Run(inline.Text))),
            InlineStyle.Code => Code(inline.Text),
            _ => new Run(inline.Text),
        };
    }

    private static Run Code(string text)
    {
        var run = new Run(text) { FontFamily = CodeFont };
        run.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        return run;
    }

    private List List(ListBlock list)
    {
        var result = new List
        {
            MarkerStyle = list.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(24, 0, 0, 0),
        };
        foreach (IReadOnlyList<MarkdownInline> item in list.Items)
        {
            Paragraph paragraph = Paragraph(item);
            paragraph.Margin = new Thickness(0, 0, 0, 4);
            result.ListItems.Add(new ListItem(paragraph));
        }

        return result;
    }

    private Table Table(TableBlock table)
    {
        var result = new Table { CellSpacing = 0, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        result.SetResourceReference(Block.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        int columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count));
        for (int i = 0; i < columns; i++)
        {
            result.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        group.Rows.Add(Row(table.Header, header: true));
        foreach (IReadOnlyList<IReadOnlyList<MarkdownInline>> row in table.Rows)
        {
            group.Rows.Add(Row(row, header: false));
        }

        result.RowGroups.Add(group);
        return result;
    }

    private TableRow Row(IReadOnlyList<IReadOnlyList<MarkdownInline>> cells, bool header)
    {
        var row = new TableRow();
        if (header)
        {
            row.FontWeight = FontWeights.SemiBold;
            row.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        }

        foreach (IReadOnlyList<MarkdownInline> cell in cells)
        {
            Paragraph paragraph = Paragraph(cell);
            paragraph.Margin = new Thickness(0);
            var tableCell = new TableCell(paragraph) { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(0, 0, 0, 1) };
            tableCell.SetResourceReference(TableCell.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            row.Cells.Add(tableCell);
        }

        return row;
    }

    private Block Picture(ImageBlock image)
    {
        ImageSource? source = loadImage(image.Source);
        if (source is null)
        {
            return new Paragraph(new Italic(new Run(image.AltText)));
        }

        var picture = new Image { Source = source, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(picture, image.AltText);
        return new BlockUIContainer(picture) { Margin = new Thickness(0, 4, 0, 12) };
    }

    private BlockUIContainer PageButton(PageButtonBlock button)
    {
        var control = new Button { Content = button.Text, HorizontalAlignment = HorizontalAlignment.Left };
        control.Click += (_, _) => Follow(button.Target);
        return new BlockUIContainer(control) { Margin = new Thickness(0, 8, 0, 0) };
    }

    private void Follow(string target)
    {
        if (target.StartsWith(TopicScheme, StringComparison.Ordinal))
        {
            openTopic(target[TopicScheme.Length..]);
        }
        else if (target.StartsWith(SettingsScheme, StringComparison.Ordinal)
            && InstanceCommand.TryParsePageName(target[SettingsScheme.Length..], out SettingsPageId page))
        {
            openPage(page);
        }
    }
}
