using System.Text;
using System.Text.RegularExpressions;

namespace HolidayLights.Core.Bulbs;

/// <summary>
/// The author and copyright of an add-on bulb as text to show and search (PO decision 4; review r1 #14). A <c>.bul</c>
/// stores them as ANSI bytes of the author's system and 5.4 showed them as Windows-1252. When the fields name a .jp (or .kr)
/// address and their bytes are valid Shift-JIS, code page 932 (or CP949), with at least one double-byte character, they
/// are read in that code page, the same rule as the add-on artist credits (Doru.bul: 桜井 達也; YesMan.bul: 김의수). Every
/// other bulb keeps its Windows-1252 text. The files themselves are never changed: Bulb Editing keeps the original bytes.
/// </summary>
public static partial class CreditText
{
    /// <summary>Shows author and copyright as text for display and search.</summary>
    /// <param name="author">The author as read (Windows-1252, as <see cref="BulFile"/> decodes it).</param>
    /// <param name="copyright">The copyright as read.</param>
    /// <returns>The fields to show; unchanged unless the East Asian rule applies.</returns>
    public static (string Author, string Copyright) Decode(string author, string copyright)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(copyright);
        string both = author + "\n" + copyright;
        int codePage = JapaneseAddress().IsMatch(both) ? 932 : KoreanAddress().IsMatch(both) ? 949 : 0;
        if (codePage == 0)
        {
            return (author, copyright);
        }

        return (DecodeOne(author, codePage), DecodeOne(copyright, codePage));
    }

    /// <summary>Reads one Windows-1252 string's bytes again in a code page; unchanged when they are not valid there or have no double-byte character.</summary>
    private static string DecodeOne(string text, int codePage)
    {
        var bytes = new byte[text.Length];
        bool high = false;
        for (int i = 0; i < text.Length; i++)
        {
            byte b = Windows1252.ToByte(text[i]);
            if (b == (byte)'?' && text[i] != '?')
            {
                return text; // Not Windows-1252 text (already decoded): leave it.
            }

            bytes[i] = b;
            high |= b >= 0x80;
        }

        if (!high || CodePagesEncodingProvider.Instance.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) is not { } encoding)
        {
            return text;
        }

        try
        {
            string decoded = encoding.GetString(bytes);
            return decoded.Length < bytes.Length ? decoded : text;
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }

    [GeneratedRegex(@"[a-z0-9-]\.jp(?![a-z0-9-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseAddress();

    [GeneratedRegex(@"[a-z0-9-]\.kr(?![a-z0-9-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KoreanAddress();
}
