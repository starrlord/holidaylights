using System.Reflection;
using System.Text.RegularExpressions;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// The Help topics (PRODUCT-SPEC 6.7, 3.10): one per <see cref="HelpTopics"/> id, titled as in <c>contents.json</c>, written
/// in the Markdown subset the Help window renders (HelpContent/README.md), with working links and no web content.
/// </summary>
public sealed partial class HelpTopicTests
{
    private static readonly string[] Pages = ["home", "bulbs", "music", "saver", "themes", "general"];

    public static TheoryData<string> TopicIds()
    {
        var data = new TheoryData<string>();
        foreach (string id in DeclaredTopicIds())
        {
            data.Add(id);
        }

        return data;
    }

    [Fact]
    public void EveryEmbeddedTopicIsInTheContents()
    {
        string[] embedded = [.. EmbeddedHelpFiles("topics/")
            .Select(n => n["topics/".Length..^".md".Length])];

        Assert.Equal(DeclaredTopicIds().Order(StringComparer.Ordinal), embedded.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void TopicStartsWithItsContentsTitle(string id)
    {
        string title = HelpContentStore.LoadContents().Books.SelectMany(b => b.Topics).Single(t => t.Id == id).Title;

        Assert.Equal($"# {title}", Lines(id)[0]);
        Assert.Single(Lines(id), l => l.StartsWith("# ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void TopicLinksPointToTopicsPagesAndPicturesOnly(string id)
    {
        string text = HelpContentStore.ReadTopicMarkdown(id);
        foreach (Match link in Link().Matches(text))
        {
            string target = link.Groups["target"].Value;
            string[] parts = target.Split(':', 2);
            switch (parts[0])
            {
                case "topic":
                    Assert.Contains(parts[1], DeclaredTopicIds());
                    Assert.NotEqual(id, parts[1]);
                    break;
                case "settings":
                    Assert.Contains(parts[1], Pages);
                    Assert.Matches(@"^Open the (Home|Bulb Factory|Music Box|Screen Saver|Themes|General) page$", link.Groups["text"].Value);
                    break;
                default:
                    Assert.True(link.Groups["image"].Success && target.StartsWith("images/", StringComparison.Ordinal), $"{id}: unexpected link target {target}");
                    Assert.True(HelpContentStore.Exists(target), $"{id}: missing picture {target}");
                    Assert.NotEmpty(link.Groups["text"].Value);
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void TopicUsesTheRenderedMarkdownSubset(string id)
    {
        string[] lines = Lines(id);
        foreach (string line in lines)
        {
            Assert.DoesNotMatch(@"^#{4,}", line);
            Assert.DoesNotMatch(@"^\s+[-*\d]", line);
            Assert.DoesNotMatch(@"^\s*(>|```|~~~|---\s*$|\*\*\*\s*$)", line);
            Assert.DoesNotMatch(@"<[A-Za-z!/]", line);
        }

        // Tables: a header row, a delimiter row, and rows with the same number of cells.
        foreach (string[] table in Tables(lines))
        {
            int columns = Cells(table[0]).Length;
            Assert.Matches(@"^\|(---\|)+$", table[1]);
            Assert.All(table, row => Assert.Equal(columns, Cells(row).Length));
        }
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void TopicHasNoWebLinksPaymentOrRetiredWindowsWords(string id)
    {
        // The project's home page is the one web address the product names (PRODUCT-SPEC 6.11).
        string text = HelpContentStore.ReadTopicMarkdown(id).Replace(ProjectInfo.HomePage, "", StringComparison.Ordinal);
        string[] forbidden =
        [
            "http:", "https:", "www.", "tigertech", "mailto:", "serial number", "$19.95", "Pay Online", "free trial",
            "Display Properties", "Sndvol32", "Start > Programs", "Internet Explorer", "Control Panel", "WinHelp",
            "up to five",
        ];
        Assert.All(forbidden, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void TopicFollowsTheHouseStyle(string id)
    {
        string text = HelpContentStore.ReadTopicMarkdown(id);

        // Labels that open a window end with the ellipsis character (PRODUCT-SPEC conventions); no emoji anywhere.
        Assert.DoesNotContain("...", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[☀-➿]|[\uD83C-\uD83E][\uDC00-\uDFFF]", text);
        Assert.False(text.Contains('\t', StringComparison.Ordinal), $"{id} contains a tab");
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPictureIsUsedByATopic()
    {
        string all = string.Concat(DeclaredTopicIds().Select(HelpContentStore.ReadTopicMarkdown));
        string[] pictures = [.. EmbeddedHelpFiles("images/")];

        Assert.NotEmpty(pictures);
        Assert.All(pictures, p => Assert.Contains($"]({p})", all, StringComparison.Ordinal));
    }

    [Fact]
    public void EverySettingsPageHasAnF1TopicThatOffersThePage()
    {
        foreach (SettingsPageId page in Enum.GetValues<SettingsPageId>())
        {
            string topic = HelpContentStore.ReadTopicMarkdown(HelpTopics.ForPage(page));
            Assert.Contains("(settings:", topic, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TaskbarIconTopicsKeepTheClickOnceAdvice()
    {
        Assert.Contains("once (not twice)", HelpContentStore.ReadTopicMarkdown(HelpTopics.TaskbarIcon), StringComparison.Ordinal);
        Assert.Contains("once (not twice)", HelpContentStore.ReadTopicMarkdown(HelpTopics.Welcome), StringComparison.Ordinal);
        Assert.Contains("up to six different kinds of bulbs", HelpContentStore.ReadTopicMarkdown(HelpTopics.AboutBulbs), StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsNewListsEveryPointOfTheSpecification()
    {
        // A Windows checkout with core.autocrlf (GitHub's runners) embeds the topic with CRLF line endings.
        string text = HelpContentStore.ReadTopicMarkdown(HelpTopics.WhatsNew).ReplaceLineEndings("\n");
        string[] points =
        [
            "Your lights fit every display again, sharp at any Windows scaling.",
            "They can sit behind your desktop icons (like before), in front of them, or on top of everything.",
            "A gentle glow and soft fading, or the Classic 2003 look with one click.",
            "Four new patterns: Twinkle, Slow Glow, Chase Around the Screen and Dance to the Music.",
            "All 1,501 add-on bulbs are included, with search, categories and favorites.",
            "Six kinds of bulbs on every edge, and you can drag them into a new order.",
            "Themes can change automatically on holidays.",
            "Real music volume and a choice of MIDI synthesizer.",
            "The screen saver runs on every display, and previewing it never changes your Windows settings.",
            "The hot key is now Ctrl+Alt+Shift+B, so web browsers keep Ctrl+Shift+B.",
            "Everything is unlocked and free, and Holiday Lights never goes online.",
        ];
        Assert.All(points, point => Assert.Contains($"- {point}\n", text, StringComparison.Ordinal));
    }

    [Fact]
    public void FlashPatternTopicDescribesEveryPatternWithItsMenuText()
    {
        string text = HelpContentStore.ReadTopicMarkdown(HelpTopics.FlashPattern);
        (string Pattern, string Line)[] patterns =
        [
            ("Don't Flash", "The bulbs stay lit."),
            ("Flash Together", "All bulbs change at the same time."),
            ("Alternating", "Every other bulb flashes."),
            ("Bulb Chase", "The lights run along each edge."),
            ("Random Flashing", "Bulbs flash at random, repeating every 8 steps like the original."),
            ("Twinkle", "Each light twinkles on its own, like real twinkle lights."),
            ("Slow Glow", "All lights slowly brighten and dim together."),
            ("Chase Around the Screen", "One light in three runs clockwise around the whole screen."),
            ("Dance to the Music", "The lights play along with the music. Between songs: Slow Glow."),
        ];
        Assert.All(patterns, p => Assert.Contains($"| **{p.Pattern}** | {p.Line} |", text, StringComparison.Ordinal));
        Assert.Contains("Stockings and Menorahs", text, StringComparison.Ordinal);
    }

    /// <summary>Embedded help files under a folder, as <see cref="HelpContentStore"/> paths (forward slashes).</summary>
    internal static IEnumerable<string> EmbeddedHelpFiles(string folder) =>
        typeof(HelpContentStore).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("help/", StringComparison.Ordinal))
            .Select(n => n["help/".Length..].Replace('\\', '/'))
            .Where(n => n.StartsWith(folder, StringComparison.Ordinal));

    private static string[] DeclaredTopicIds() =>
        [.. typeof(HelpTopics).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)];

    private static string[] Lines(string id) =>
        HelpContentStore.ReadTopicMarkdown(id).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    private static IEnumerable<string[]> Tables(string[] lines)
    {
        var table = new List<string>();
        foreach (string line in lines.Append(""))
        {
            if (line.StartsWith('|'))
            {
                table.Add(line);
                continue;
            }

            if (table.Count > 0)
            {
                yield return [.. table];
                table.Clear();
            }
        }
    }

    private static string[] Cells(string row) => row.Trim('|').Split('|');

    [GeneratedRegex(@"(?<image>!)?\[(?<text>[^\]]*)\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Link();
}
