using HolidayLights.App.Help;

namespace HolidayLights.Tests.App;

/// <summary>The Help window's Markdown, search and history (PRODUCT-SPEC 3.10, 6.7; <c>HelpContent/README.md</c>).</summary>
public sealed class HelpTests
{
    [Fact]
    public void HeadingsParagraphsAndInlines()
    {
        MarkdownDocument document = MarkdownParser.Parse("# Title\n\nSome **bold** and *italic* text with `code`\nthat continues.\n\n## Part\n\n### Detail");
        Assert.Equal("Title", document.Title);
        Assert.Collection(
            document.Blocks,
            b => Assert.Equal(1, Assert.IsType<HeadingBlock>(b).Level),
            b =>
            {
                ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(b);
                Assert.Equal(
                    [
                        new MarkdownInline("Some "),
                        new MarkdownInline("bold", InlineStyle.Bold),
                        new MarkdownInline(" and "),
                        new MarkdownInline("italic", InlineStyle.Italic),
                        new MarkdownInline(" text with "),
                        new MarkdownInline("code", InlineStyle.Code),
                        new MarkdownInline(" that continues."),
                    ],
                    paragraph.Inlines);
            },
            b => Assert.Equal(2, Assert.IsType<HeadingBlock>(b).Level),
            b => Assert.Equal(3, Assert.IsType<HeadingBlock>(b).Level));
    }

    [Fact]
    public void CodeKeepsStarsAndPipes()
    {
        IReadOnlyList<MarkdownInline> inlines = MarkdownParser.ParseInlines("These characters can't be used: `\\ / : * ? \" < > |`");
        Assert.Equal(new MarkdownInline("\\ / : * ? \" < > |", InlineStyle.Code), inlines[^1]);
    }

    [Fact]
    public void LinksToTopicsAndPages()
    {
        IReadOnlyList<MarkdownInline> inlines = MarkdownParser.ParseInlines("Read [The Taskbar Icon](topic:taskbar-icon) now.");
        Assert.Equal(new MarkdownInline("The Taskbar Icon", InlineStyle.Normal, "topic:taskbar-icon"), inlines[1]);

        MarkdownDocument document = MarkdownParser.Parse("Text.\n\n[Open the Bulb Factory page](settings:bulbs)");
        Assert.Equal(new PageButtonBlock("Open the Bulb Factory page", "settings:bulbs"), document.Blocks[^1]);
    }

    [Fact]
    public void ListsAndTheirContinuationLines()
    {
        MarkdownDocument document = MarkdownParser.Parse("- one\n- two\n  more\n* three\n\n1. first\n2. second");
        ListBlock bullets = Assert.IsType<ListBlock>(document.Blocks[0]);
        Assert.False(bullets.Ordered);
        Assert.Equal(["one", "two more", "three"], bullets.Items.Select(MarkdownDocument.PlainText));
        ListBlock numbers = Assert.IsType<ListBlock>(document.Blocks[1]);
        Assert.True(numbers.Ordered);
        Assert.Equal(2, numbers.Items.Count);
    }

    [Fact]
    public void BoldTextAtTheStartOfALineIsNotAList()
    {
        MarkdownDocument document = MarkdownParser.Parse("**Flavors.** A side can have up to eight.");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(new MarkdownInline("Flavors.", InlineStyle.Bold), paragraph.Inlines[0]);
    }

    [Fact]
    public void TablesWithEscapedPipes()
    {
        MarkdownDocument document = MarkdownParser.Parse("| Item | What it does |\n|---|---|\n| **Show Lights** | On \\| off |\n| Next | Song |");
        TableBlock table = Assert.IsType<TableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(["Item", "What it does"], table.Header.Select(MarkdownDocument.PlainText));
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new MarkdownInline("Show Lights", InlineStyle.Bold), table.Rows[0][0][0]);
        Assert.Equal("On | off", MarkdownDocument.PlainText(table.Rows[0][1]));
    }

    [Fact]
    public void PicturesOnTheirOwnLine()
    {
        MarkdownDocument document = MarkdownParser.Parse("![The Holiday Lights menu](images/tray-menu.png)");
        Assert.Equal(new ImageBlock("images/tray-menu.png", "The Holiday Lights menu"), Assert.Single(document.Blocks));
    }

    [Fact]
    public void EveryTopicParsesWithItsTitleAndValidLinks()
    {
        HelpContents contents = HelpContentStore.LoadContents();
        var ids = new HashSet<string>(contents.Books.SelectMany(b => b.Topics).Select(t => t.Id), StringComparer.Ordinal);
        foreach (HelpTopicEntry topic in contents.Books.SelectMany(b => b.Topics))
        {
            MarkdownDocument document = MarkdownParser.Parse(HelpContentStore.ReadTopicMarkdown(topic.Id));
            Assert.Equal(topic.Title, document.Title);

            IEnumerable<string> targets = document.Blocks.SelectMany(LinkTargets);
            foreach (string target in targets)
            {
                if (target.StartsWith("topic:", StringComparison.Ordinal))
                {
                    Assert.Contains(target["topic:".Length..], ids);
                }
                else
                {
                    Assert.StartsWith("settings:", target);
                    Assert.True(InstanceCommand.TryParsePageName(target["settings:".Length..], out _), $"{topic.Id}: {target}");
                }
            }

            foreach (ImageBlock image in document.Blocks.OfType<ImageBlock>())
            {
                Assert.True(HelpContentStore.Exists(image.Source), $"{topic.Id}: {image.Source}");
            }
        }
    }

    [Fact]
    public void SearchFindsTitlesFirstThenText()
    {
        var index = new HelpSearchIndex(
        [
            ("a", "Volume", "Changes how loud the music plays."),
            ("b", "About Music", "The music volume is set in the Music Box. Music, music."),
            ("c", "Screen Saver", "Pictures and snow."),
        ]);
        Assert.Equal(["a", "b"], index.Search("volume").Select(r => r.TopicId));
        Assert.Equal(["b", "a"], index.Search("music").Select(r => r.TopicId));
        Assert.Empty(index.Search("   "));
        Assert.Empty(index.Search("reindeer"));
        Assert.Equal(["c"], index.Search("SNOW pictures").Select(r => r.TopicId));
        Assert.Contains("snow", index.Search("snow")[0].Snippet);
    }

    [Fact]
    public void SearchIgnoresAccents() =>
        Assert.Single(new HelpSearchIndex([("x", "Café", "Lights at the café.")]).Search("cafe"));

    [Fact]
    public void BackAndForward()
    {
        var history = new HelpHistory();
        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);

        history.Navigate("welcome");
        history.Navigate("taskbar-icon");
        history.Navigate("taskbar-icon");
        Assert.True(history.CanGoBack);
        Assert.Equal("welcome", history.Back());
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);
        Assert.Equal("taskbar-icon", history.Forward());
        Assert.Null(history.Forward());

        history.Back();
        history.Navigate("volume");
        Assert.False(history.CanGoForward);
        Assert.Equal("welcome", history.Back());
    }

    private static IEnumerable<string> LinkTargets(MarkdownBlock block) => block switch
    {
        ParagraphBlock p => p.Inlines.Select(i => i.LinkTarget).OfType<string>(),
        HeadingBlock h => h.Inlines.Select(i => i.LinkTarget).OfType<string>(),
        ListBlock l => l.Items.SelectMany(i => i).Select(i => i.LinkTarget).OfType<string>(),
        TableBlock t => t.Rows.SelectMany(r => r).SelectMany(c => c).Select(i => i.LinkTarget).OfType<string>(),
        PageButtonBlock b => [b.Target],
        _ => [],
    };
}
