namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// A Bulb List search (PRODUCT-SPEC 3.2.5): every word must match the name, a category, the description, the author or
/// the file name; results rank "Best Match": name prefix, name word, name substring, category, then the other fields.
/// </summary>
internal sealed class SearchQuery
{
    /// <summary>Rank of a bulb whose name starts with the search.</summary>
    public const int NamePrefix = 0;

    /// <summary>Rank of a bulb with a name word that starts with a search word.</summary>
    public const int NameWord = 1;

    /// <summary>Rank of a bulb whose name contains a search word.</summary>
    public const int NameSubstring = 2;

    /// <summary>Rank of a bulb with a category that contains a search word.</summary>
    public const int Category = 3;

    /// <summary>Rank of a bulb whose description, author or file name contains a search word.</summary>
    public const int OtherField = 4;

    /// <summary>Rank of a bulb that does not match.</summary>
    public const int NoMatch = -1;

    private readonly string phrase;
    private readonly string[] words;

    private SearchQuery(string phrase, string[] words)
    {
        this.phrase = phrase;
        this.words = words;
    }

    /// <summary>Parses search text.</summary>
    /// <param name="text">What the user typed.</param>
    /// <returns>The query, or null when the text holds no word.</returns>
    public static SearchQuery? Parse(string? text)
    {
        string[] words = SearchText.Fold(text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? null : new SearchQuery(string.Join(' ', words), words);
    }

    /// <summary>Ranks one bulb: the weakest field any search word needed, or <see cref="NoMatch"/>.</summary>
    /// <param name="text">The bulb's folded text.</param>
    /// <param name="categories">The bulb's effective categories, folded.</param>
    /// <returns>A rank from <see cref="NamePrefix"/> to <see cref="OtherField"/>, or <see cref="NoMatch"/>.</returns>
    public int Rank(SearchText text, IReadOnlyList<string> categories)
    {
        if (text.Name.StartsWith(phrase, StringComparison.Ordinal))
        {
            return NamePrefix;
        }

        int rank = NamePrefix;
        foreach (string word in words)
        {
            int wordRank = RankWord(text, categories, word);
            if (wordRank == NoMatch)
            {
                return NoMatch;
            }

            rank = Math.Max(rank, wordRank);
        }

        return rank;
    }

    private static int RankWord(SearchText text, IReadOnlyList<string> categories, string word)
    {
        if (text.Name.StartsWith(word, StringComparison.Ordinal))
        {
            return NamePrefix;
        }

        if (text.NameWords.Any(w => w.StartsWith(word, StringComparison.Ordinal)))
        {
            return NameWord;
        }

        if (text.Name.Contains(word, StringComparison.Ordinal))
        {
            return NameSubstring;
        }

        if (categories.Any(c => c.Contains(word, StringComparison.Ordinal)))
        {
            return Category;
        }

        return text.Other.Contains(word, StringComparison.Ordinal) ? OtherField : NoMatch;
    }
}
