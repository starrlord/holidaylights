namespace HolidayLights.Core.Bulbs;

/// <summary>
/// Windows-1252 conversion as 5.4 saw its strings (<c>MultiByteToWideChar(1252)</c>): the five bytes that code page 1252
/// leaves undefined (0x81, 0x8D, 0x8F, 0x90, 0x9D) map to the C1 control characters of the same value. The mapping is a
/// bijection on all 256 byte values, so names can be turned back into their original bytes for 5.4 sorting.
/// </summary>
/// <remarks>Self-contained so that the library does not register a process-wide code page provider.</remarks>
internal static class Windows1252
{
    // Code points of the bytes 0x80-0x9F (0xA0-0xFF equal their Latin-1 code points).
    private static readonly ushort[] HighHalf =
    [
        0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
        0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178,
    ];

    private static readonly Dictionary<char, byte> HighHalfBytes = BuildReverseMap();

    /// <summary>Decodes bytes up to (not including) the first NUL, or all of them when there is none.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    public static string DecodeCString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Decode(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>Decodes every byte.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return "";
        }

        Span<char> chars = bytes.Length <= 512 ? stackalloc char[bytes.Length] : new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = ToChar(bytes[i]);
        }

        return new string(chars);
    }

    /// <summary>Returns the character of one byte.</summary>
    /// <param name="value">The byte.</param>
    /// <returns>The character.</returns>
    public static char ToChar(byte value) => value is >= 0x80 and < 0xA0 ? (char)HighHalf[value - 0x80] : (char)value;

    /// <summary>Returns the byte of one character, or '?' (0x3F) for a character outside the code page.</summary>
    /// <param name="value">The character.</param>
    /// <returns>The byte.</returns>
    public static byte ToByte(char value)
    {
        if (value < 0x80 || value is >= (char)0xA0 and <= (char)0xFF)
        {
            return (byte)value;
        }

        return HighHalfBytes.TryGetValue(value, out byte b) ? b : (byte)'?';
    }

    private static Dictionary<char, byte> BuildReverseMap()
    {
        var map = new Dictionary<char, byte>(HighHalf.Length);
        for (int i = 0; i < HighHalf.Length; i++)
        {
            map[(char)HighHalf[i]] = (byte)(0x80 + i);
        }

        return map;
    }
}
