using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// Composes <c>docs/USER-GUIDE.md</c> from the Help: the books and topics of <c>contents.json</c> in order, so the guide
/// and the in-app Help always say the same thing. The branding generator writes the guide; the Branding tests check that
/// the committed guide is up to date.
/// </summary>
/// <remarks>
/// Topic headings move down two levels under their book (one level when a book holds a single topic of the same name,
/// whose own heading is then dropped); <c>topic:</c> links become links to the topic's heading (GitHub heading anchors);
/// <c>settings:</c> page buttons become a sentence naming the page; picture paths point into <c>HelpContent/images</c>.
/// </remarks>
internal static partial class UserGuideComposer
{
    /// <summary>Where the guide lives relative to the repository root.</summary>
    public const string GuidePath = "docs/USER-GUIDE.md";

    private const string ImagesFromDocs = "../src/HolidayLights.App/HelpContent/images/";

    private static readonly Dictionary<string, string> PageNames = new(StringComparer.Ordinal)
    {
        ["home"] = "Home",
        ["bulbs"] = "Bulb Factory",
        ["music"] = "Music Box",
        ["saver"] = "Screen Saver",
        ["themes"] = "Themes",
        ["general"] = "General",
    };

    /// <summary>Composes the guide.</summary>
    /// <param name="contentsJson">The text of <c>HelpContent/contents.json</c>.</param>
    /// <param name="readTopic">Reads the Markdown of a topic by id.</param>
    /// <returns>The guide (LF line endings).</returns>
    public static string Compose(string contentsJson, Func<string, string> readTopic)
    {
        List<(string Title, List<(string Id, string Title)> Topics)> books = ReadContents(contentsJson);
        var lines = new List<string>
        {
            "# Holiday Lights Modern Edition 6.0 - User Guide",
            "",
            "Holiday Lights decorates the edges of your screen with strings of festive bulbs, plays holiday music if you like, and shows a cheerful screen saver when your computer is idle. This guide covers everything in Holiday Lights - Modern Edition 6.0 for Windows 11.",
            "",
            "It is the same text as the Help inside the program: click the red bulb in the notification area and choose **Holiday Lights Help**, or press F1 in Holiday Lights Settings.",
            "",
            "## Contents",
            "",
        };

        // Placeholders: the anchors are only known once every heading of the document has been emitted.
        foreach ((string title, List<(string Id, string Title)> topics) in books)
        {
            lines.Add($"- [{title}](#book:{title})");
            if (!IsMerged(title, topics))
            {
                lines.AddRange(topics.Select(t => $"  - [{t.Title}](#topic:{t.Id})"));
            }
        }

        var topicHeadings = new Dictionary<string, int>(StringComparer.Ordinal);
        var bookHeadings = new Dictionary<string, int>(StringComparer.Ordinal);
        var headings = new List<string> { "Holiday Lights Modern Edition 6.0 - User Guide", "Contents" };
        foreach ((string title, List<(string Id, string Title)> topics) in books)
        {
            lines.Add("");
            bookHeadings[title] = headings.Count;
            headings.Add(title);
            lines.Add($"## {title}");
            bool merged = IsMerged(title, topics);
            foreach ((string id, _) in topics)
            {
                if (merged)
                {
                    topicHeadings[id] = bookHeadings[title];
                }

                AppendTopic(lines, headings, topicHeadings, id, readTopic(id), merged ? 1 : 2);
            }
        }

        string[] anchors = Anchors(headings);
        var text = new StringBuilder();
        foreach (string line in lines)
        {
            string resolved = PlaceholderLink().Replace(line, m => m.Groups["kind"].Value == "book"
                ? $"(#{anchors[bookHeadings[m.Groups["target"].Value]]})"
                : $"(#{anchors[topicHeadings[m.Groups["target"].Value]]})");
            text.Append(resolved).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>GitHub's heading anchors: lower case, punctuation removed, spaces as hyphens, repeats numbered.</summary>
    /// <param name="headings">Heading texts in document order.</param>
    /// <returns>The anchors, in the same order.</returns>
    public static string[] Anchors(IEnumerable<string> headings)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var anchors = new List<string>();
        foreach (string heading in headings)
        {
            string slug = string.Concat(heading.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')).Replace(' ', '-');
            int count = seen.GetValueOrDefault(slug);
            seen[slug] = count + 1;
            anchors.Add(count == 0 ? slug : $"{slug}-{count.ToString(CultureInfo.InvariantCulture)}");
        }

        return [.. anchors];
    }

    private static bool IsMerged(string bookTitle, List<(string Id, string Title)> topics) =>
        topics.Count == 1 && string.Equals(topics[0].Title, bookTitle, StringComparison.Ordinal);

    private static void AppendTopic(List<string> lines, List<string> headings, Dictionary<string, int> topicHeadings, string id, string markdown, int shift)
    {
        string[] source = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        foreach (string line in source)
        {
            Match heading = Heading().Match(line);
            if (heading.Success)
            {
                int level = heading.Groups["hashes"].Length;
                string title = heading.Groups["text"].Value;
                if (level == 1 && shift == 1)
                {
                    continue;
                }

                if (level == 1)
                {
                    topicHeadings[id] = headings.Count;
                }

                if (lines[^1].Length != 0)
                {
                    lines.Add("");
                }

                headings.Add(title);
                lines.Add($"{new string('#', level + shift)} {title}");
                continue;
            }

            Match page = PageButton().Match(line);
            if (page.Success)
            {
                lines.Add($"*In Holiday Lights: the {PageNames[page.Groups["page"].Value]} page of Holiday Lights Settings.*");
                continue;
            }

            lines.Add(TopicLink().Replace(line, "(#topic:${id})").Replace("](images/", $"]({ImagesFromDocs}", StringComparison.Ordinal));
        }
    }

    private static List<(string Title, List<(string Id, string Title)> Topics)> ReadContents(string contentsJson)
    {
        using JsonDocument document = JsonDocument.Parse(contentsJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return [.. document.RootElement.GetProperty("books").EnumerateArray().Select(book => (
            book.GetProperty("title").GetString()!,
            book.GetProperty("topics").EnumerateArray().Select(t => (t.GetProperty("id").GetString()!, t.GetProperty("title").GetString()!)).ToList()))];
    }

    [GeneratedRegex(@"^(?<hashes>#{1,3}) (?<text>.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\[Open the [^\]]+ page\]\(settings:(?<page>[a-z]+)\)$")]
    private static partial Regex PageButton();

    [GeneratedRegex(@"\(topic:(?<id>[a-z0-9-]+)\)")]
    private static partial Regex TopicLink();

    [GeneratedRegex(@"\(#(?<kind>book|topic):(?<target>[^)]+)\)")]
    private static partial Regex PlaceholderLink();
}
