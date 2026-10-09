using System.Globalization;
using System.Text;

namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// The searchable text of one bulb, folded once (lower case, accents removed) so that the Bulb List search
/// (PRODUCT-SPEC 3.2.5) is case- and accent-insensitive.
/// </summary>
/// <param name="Name">Folded name.</param>
/// <param name="NameWords">Folded words of the name (split at anything that is not a letter or digit).</param>
/// <param name="Other">Folded description, author and file name, one per line.</param>
internal sealed record SearchText(string Name, IReadOnlyList<string> NameWords, string Other)
{
    /// <summary>Folds the searchable fields of a bulb.</summary>
    /// <param name="name">Name.</param>
    /// <param name="description">Description.</param>
    /// <param name="author">Author.</param>
    /// <param name="fileName">File name without extension ("" for built-ins).</param>
    /// <returns>The folded text.</returns>
    public static SearchText For(string name, string description, string author, string fileName)
    {
        string folded = Fold(name);
        return new SearchText(folded, Words(folded), string.Join('\n', Fold(description), Fold(author), Fold(fileName)));
    }

    /// <summary>Lower case without diacritics: "Stj&#228;rna" becomes "stjarna".</summary>
    /// <param name="text">Any text.</param>
    /// <returns>The folded text.</returns>
    public static string Fold(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Splits folded text into words at every character that is not a letter or digit.</summary>
    /// <param name="folded">Folded text.</param>
    /// <returns>The words.</returns>
    public static IReadOnlyList<string> Words(string folded)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i <= folded.Length; i++)
        {
            bool inWord = i < folded.Length && char.IsLetterOrDigit(folded[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(folded[start..i]);
                start = -1;
            }
        }

        return words;
    }
}
