using System.Globalization;

namespace HolidayLights.App.Help;

/// <summary>One search result.</summary>
/// <param name="TopicId">The topic.</param>
/// <param name="Title">Its title.</param>
/// <param name="Snippet">Text around the first match in the topic text, or empty for a title match.</param>
public sealed record HelpSearchResult(string TopicId, string Title, string Snippet);

/// <summary>
/// Searches the titles and text of every help topic (PRODUCT-SPEC 3.10: "Ctrl+F searches titles and text"): every word of
/// the query must occur (case- and accent-insensitive); topics whose title matches come first, then by the number of
/// matches. Thread-safe once built.
/// </summary>
public sealed class HelpSearchIndex
{
    private const int SnippetRadius = 60;
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly IReadOnlyList<(string Id, string Title, string Text)> topics;

    /// <summary>Creates the index.</summary>
    /// <param name="topics">Every topic: id, title and plain text, in Contents order.</param>
    public HelpSearchIndex(IEnumerable<(string Id, string Title, string Text)> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        this.topics = [.. topics];
    }

    /// <summary>Finds the topics that contain every word of a query.</summary>
    /// <param name="query">The words.</param>
    /// <returns>The results, best first; empty for an empty query.</returns>
    public IReadOnlyList<HelpSearchResult> Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var results = new List<(HelpSearchResult Result, bool TitleMatch, int Hits, int Order)>();
        for (int order = 0; order < topics.Count; order++)
        {
            (string id, string title, string text) = topics[order];
            if (!words.All(w => Contains(title, w) || Contains(text, w)))
            {
                continue;
            }

            bool titleMatch = words.All(w => Contains(title, w));
            int hits = words.Sum(w => CountOf(text, w));
            results.Add((new HelpSearchResult(id, title, titleMatch ? "" : Snippet(text, words[0])), titleMatch, hits, order));
        }

        return [.. results
            .OrderByDescending(r => r.TitleMatch)
            .ThenByDescending(r => r.Hits)
            .ThenBy(r => r.Order)
            .Select(r => r.Result)];
    }

    private static bool Contains(string text, string word) => Compare.IndexOf(text, word, Options) >= 0;

    private static int CountOf(string text, string word)
    {
        int count = 0;
        for (int index = Compare.IndexOf(text, word, Options); index >= 0 && index < text.Length; index = Compare.IndexOf(text, word, index + 1, Options))
        {
            count++;
        }

        return count;
    }

    private static string Snippet(string text, string word)
    {
        int index = Compare.IndexOf(text, word, Options);
        if (index < 0)
        {
            return "";
        }

        int start = Math.Max(0, index - SnippetRadius);
        int end = Math.Min(text.Length, index + word.Length + SnippetRadius);
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return (start > 0 ? "…" : "") + text[start..end].Trim() + (end < text.Length ? "…" : "");
    }
}
