using System.Text;

namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// The text of a <c>.bul</c> file as Holiday Lights 5.4 stores it: Windows-1252, one
/// byte per character, read back by <see cref="BulFile"/> with the same mapping. A character the code page cannot hold
/// (or a NUL, which would end the C string early) is stored as "?" (PRODUCT-SPEC 6.3).
/// </summary>
public static class BulText
{
    /// <summary>Longest header text (name, description, copyright, author): 79 characters plus the terminating NUL in an 80-byte field.</summary>
    public const int MaxFieldLength = 79;

    private const byte Replacement = (byte)'?';

    /// <summary>True when every character of the text can be stored in a bulb file unchanged.</summary>
    /// <param name="text">The text.</param>
    /// <returns>False when a character would be replaced by "?".</returns>
    public static bool CanStore(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!TryGetByte(rune, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Encodes text as Windows-1252; characters outside the code page and NUL become "?".</summary>
    /// <param name="text">The text.</param>
    /// <param name="replaced">True when at least one character was replaced.</param>
    /// <returns>One byte per character (a character outside the Basic Multilingual Plane is one "?").</returns>
    public static byte[] Encode(string text, out bool replaced)
    {
        ArgumentNullException.ThrowIfNull(text);
        replaced = false;
        var bytes = new List<byte>(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (TryGetByte(rune, out byte value))
            {
                bytes.Add(value);
            }
            else
            {
                bytes.Add(Replacement);
                replaced = true;
            }
        }

        return [.. bytes];
    }

    /// <summary>Encodes a header text and cuts it to <see cref="MaxFieldLength"/> bytes (5.4 read the edits with a limit of 80 bytes including the NUL).</summary>
    /// <param name="text">The text.</param>
    /// <param name="replaced">True when a character in the stored part was replaced.</param>
    /// <returns>At most 79 bytes.</returns>
    internal static byte[] EncodeField(string text, out bool replaced) => Encode(Truncate(text, MaxFieldLength), out replaced);

    /// <summary>The first characters (runes, each stored as one byte) of a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxCharacters">How many characters to keep.</param>
    /// <returns>The text, cut after <paramref name="maxCharacters"/> characters.</returns>
    internal static string Truncate(string text, int maxCharacters)
    {
        int count = 0;
        int end = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (count == maxCharacters)
            {
                return text[..end];
            }

            count++;
            end += rune.Utf16SequenceLength;
        }

        return text;
    }

    private static bool TryGetByte(Rune rune, out byte value)
    {
        value = Replacement;
        if (!rune.IsBmp || rune.Value == 0)
        {
            return false;
        }

        char c = (char)rune.Value;
        value = Windows1252.ToByte(c);
        return value != Replacement || c == '?';
    }
}
