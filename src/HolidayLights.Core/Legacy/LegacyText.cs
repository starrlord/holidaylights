using System.Text;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// Decodes the ANSI strings 5.4 kept in binary registry values (song lists, the LOGFONT face name) as Windows-1252, the
/// code page of every known 5.4 installation (bytes undefined in 1252 map to U+0081 etc., like <c>MultiByteToWideChar</c>).
/// </summary>
internal static class LegacyText
{
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

    /// <summary>Decodes a NUL-terminated string (bytes after the NUL are ignored).</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The string.</returns>
    public static string DecodeTerminated(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Windows1252.GetString(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>
    /// Decodes a 5.4 song list (<c>name\0name\0...\0\0</c>; an empty list is a single 0 byte). Like 5.4, the list ends at
    /// the first empty name.
    /// </summary>
    /// <param name="bytes">The value.</param>
    /// <returns>The names in stored order.</returns>
    public static IReadOnlyList<string> DecodeList(ReadOnlySpan<byte> bytes)
    {
        var names = new List<string>();
        while (!bytes.IsEmpty && bytes[0] != 0)
        {
            int end = bytes.IndexOf((byte)0);
            names.Add(Windows1252.GetString(end < 0 ? bytes : bytes[..end]));
            bytes = end < 0 ? [] : bytes[(end + 1)..];
        }

        return names;
    }
}
