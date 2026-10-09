namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// The 5.4 add-on list order (<c>BulbList_Insert</c> with <c>Str_ICompare</c>): names compared as
/// Windows-1252 bytes, A-Z lowered to a-z (C locale <c>tolower</c>), unsigned byte by byte, a prefix before longer names.
/// </summary>
internal sealed class LegacyNameComparer : IComparer<string>
{
    /// <summary>The shared instance.</summary>
    public static LegacyNameComparer Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        x ??= "";
        y ??= "";
        int length = Math.Min(x.Length, y.Length);
        for (int i = 0; i < length; i++)
        {
            int a = Lower(Windows1252.ToByte(x[i]));
            int b = Lower(Windows1252.ToByte(y[i]));
            if (a != b)
            {
                return a - b;
            }
        }

        return x.Length - y.Length;
    }

    private static int Lower(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? value + 32 : value;
}
