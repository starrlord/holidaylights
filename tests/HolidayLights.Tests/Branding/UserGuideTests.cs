using System.Text.RegularExpressions;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// <c>docs/USER-GUIDE.md</c> is the Help as one document: it must equal what <see cref="UserGuideComposer"/> makes from
/// the embedded Help, so the guide and the in-app pages never drift apart. After changing a topic, run the branding
/// generator (<c>BrandingGenerator user-guide</c>).
/// </summary>
public sealed partial class UserGuideTests
{
    private static readonly Lazy<string> Composed = new(() =>
    {
        using var reader = new StreamReader(HelpContentStore.Open("contents.json"));
        return UserGuideComposer.Compose(reader.ReadToEnd(), HelpContentStore.ReadTopicMarkdown);
    });

    [Fact]
    public void GuideIsUpToDateWithTheHelp()
    {
        string committed = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, UserGuideComposer.GuidePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(Composed.Value, committed);
    }

    [Fact]
    public void EveryLinkOfTheGuideLandsOnAHeadingOrAPicture()
    {
        string guide = Composed.Value;
        string[] anchors = UserGuideComposer.Anchors(Heading().Matches(guide).Select(m => m.Groups["text"].Value));
        Assert.Equal(anchors.Length, anchors.Distinct().Count());

        foreach (Match link in Link().Matches(guide))
        {
            string target = link.Groups["target"].Value;
            if (target.StartsWith('#'))
            {
                Assert.Contains(target[1..], anchors);
            }
            else
            {
                Assert.StartsWith("../src/HolidayLights.App/HelpContent/images/", target, StringComparison.Ordinal);
                Assert.True(File.Exists(Path.Combine(TestPaths.RepoRoot, "docs", target)), target);
            }
        }
    }

    [Fact]
    public void GuideHasEveryTopicAndNoInAppLinks()
    {
        string guide = Composed.Value;

        Assert.All(HelpContentStore.LoadContents().Books.SelectMany(b => b.Topics), t => Assert.Contains($"[{t.Title}](#", guide, StringComparison.Ordinal));
        Assert.DoesNotContain("(topic:", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("(settings:", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("(images/", guide, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^#{1,6} (?<text>.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\[[^\]]*\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Link();
}
