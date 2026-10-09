using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// The "Add-On Bulb Artists" credits (PRODUCT-SPEC 6.5 item 3): every distinct author of the bundled add-on bulbs, A-Z
/// with bulb counts, from the first line of each <c>.bul</c> author field. The branding generator writes them into
/// <c>credits.json</c> and the "Art Copyright Information" topic; the Branding tests check both against the bulb files.
/// </summary>
/// <remarks>
/// Rules, in order: the first line, with runs of white space collapsed and trimmed; "Created by" (also "Created with
/// ... by" and "Special Request created ... by") and "By" removed; a leading "Copyright" label and date and a trailing
/// "Month day year" date removed (one artist dated every credit); an e-mail-only entry becomes the part before "@", and an
/// e-mail address after a name is removed with its separator; trailing periods and separators removed. Entries are merged
/// case-, space- and punctuation-insensitively, showing the most frequent spelling. Two files carry their artists' names
/// in an East Asian code page (their e-mail domains are .jp and .kr) and are read with it; every other file is read as
/// Windows-1252, like Holiday Lights itself.
/// </remarks>
internal static partial class AddOnArtistCredits
{
    private const int AuthorOffset = 0x10C;
    private const int FieldLength = 80;

    /// <summary>Files whose author field is not Windows-1252 text.</summary>
    private static readonly Dictionary<string, int> CodePageByFile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Doru.bul"] = 932,
        ["YesMan.bul"] = 949,
    };

    /// <summary>A first line that runs a name and an e-mail address together, with the name it means.</summary>
    private static readonly Dictionary<string, string> Spellings = new(StringComparer.Ordinal)
    {
        ["D. StotzCDSIV@aol.com"] = "D. Stotz",
    };

    static AddOnArtistCredits() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Computes the credits of every <c>.bul</c> file in a folder.</summary>
    /// <param name="folder">The bundled bulbs (<c>Content\Bulbs</c> next to the program, or <c>content\Bulbs</c> in the repository).</param>
    /// <returns>Artists A-Z (case-insensitive) with their bulb counts.</returns>
    public static IReadOnlyList<(string Name, int Count)> FromFolder(string folder)
    {
        var spellings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(folder, "*.bul"))
        {
            string name = Artist(ReadAuthorField(file));
            string key = MergeKey(name);
            if (key.Length == 0)
            {
                continue;
            }

            if (!spellings.TryGetValue(key, out Dictionary<string, int>? counts))
            {
                counts = new Dictionary<string, int>(StringComparer.Ordinal);
                spellings[key] = counts;
            }

            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return [.. spellings.Values
            .Select(counts => (Name: counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).First().Key, Count: counts.Values.Sum()))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Name, StringComparer.Ordinal)];
    }

    /// <summary>Reads the author field (header offset 0x10C, 80 bytes, NUL-terminated) of a <c>.bul</c> file.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The field text.</returns>
    public static string ReadAuthorField(string file)
    {
        var header = new byte[AuthorOffset + FieldLength];
        using (FileStream stream = File.OpenRead(file))
        {
            stream.ReadExactly(header);
        }

        ReadOnlySpan<byte> field = header.AsSpan(AuthorOffset, FieldLength);
        int end = field.IndexOf((byte)0);
        int codePage = CodePageByFile.GetValueOrDefault(Path.GetFileName(file), 1252);
        return Encoding.GetEncoding(codePage).GetString(end < 0 ? field : field[..end]);
    }

    /// <summary>The artist credited by an author field.</summary>
    /// <param name="authorField">The whole author field (name, then often an e-mail address on the next line).</param>
    /// <returns>The artist as shown in the credits.</returns>
    public static string Artist(string authorField)
    {
        string line = authorField.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)[0];
        string text = Whitespace().Replace(line, " ").Trim();
        text = Spellings.GetValueOrDefault(text, text);
        text = CreatedBy().Replace(text, "");
        text = By().Replace(text, "");
        text = CopyrightLabel().Replace(text, "");
        text = TrailingDate().Replace(text, "");
        text = EmailOnly().IsMatch(text) ? text[..text.IndexOf('@', StringComparison.Ordinal)] : EmailAfterName().Replace(text, "");
        return TrailingPunctuation().Replace(text, "").Trim();
    }

    /// <summary>The key under which spellings of one artist merge: letters and digits only, lower case.</summary>
    /// <param name="name">An artist.</param>
    /// <returns>The key.</returns>
    public static string MergeKey(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit).Select(c => char.ToLower(c, CultureInfo.InvariantCulture)));

    /// <summary>
    /// The "Add-On Bulb Artists" section of the Art Copyright Information topic: the artists grouped by initial, each
    /// group a paragraph of "Name (count)" entries.
    /// </summary>
    /// <param name="artists">The artists, A-Z.</param>
    /// <returns>Markdown, starting with the section heading and ending with a line break.</returns>
    public static string MarkdownSection(IReadOnlyList<(string Name, int Count)> artists)
    {
        var text = new StringBuilder();
        text.Append("## Add-On Bulb Artists\n\n");
        text.Append(CultureInfo.GetCultureInfo("en-US"), $"The {artists.Sum(a => a.Count):N0} add-on bulbs were drawn by {artists.Count} artists. Thank you, every one of you!\n");
        foreach (IGrouping<string, (string Name, int Count)> group in artists.GroupBy(a => Initial(a.Name)).OrderBy(g => g.Key == "Other" ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append("\n**").Append(group.Key).Append("**: ");
            text.Append(string.Join("; ", group.Select(a => $"{a.Name} ({a.Count})")));
            text.Append('\n');
        }

        return text.ToString();
    }

    private static string Initial(string name)
    {
        char first = char.ToUpperInvariant(name[0]);
        return first is >= 'A' and <= 'Z' ? first.ToString() : "Other";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(?:special request\s+)?created(?:\s+with\s+[^.]*?)?\s+by\s+", RegexOptions.IgnoreCase)]
    private static partial Regex CreatedBy();

    [GeneratedRegex(@"^by:?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex By();

    [GeneratedRegex(@"^(?:copyright|copyight)\b[:\s]*(?:\d{1,2}/[A-Za-z]{3}/\d{4}\s*)?", RegexOptions.IgnoreCase)]
    private static partial Regex CopyrightLabel();

    [GeneratedRegex(@"\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingDate();

    [GeneratedRegex(@"^[^\s,;/\\()<>]+@[^\s,;/\\()<>]+\.[^\s,;/\\()<>]+$")]
    private static partial Regex EmailOnly();

    [GeneratedRegex(@"[\s,;/\\(:-]*(?:\bat:?\s*)?[^\s,;/\\()<>]+@[^\s,;/\\()<>]+\.[^\s,;/\\()<>]+\)?")]
    private static partial Regex EmailAfterName();

    [GeneratedRegex(@"[\s.,;:]+$")]
    private static partial Regex TrailingPunctuation();
}
