namespace HolidayLights.Audio.Library;

/// <summary>Which files are songs, their "Type" column, and the 5.4 sort key (PRODUCT-SPEC 3.4.3).</summary>
internal static class SongFileTypes
{
    /// <summary>The extension of Windows shortcuts; a shortcut is a song when its target is one.</summary>
    public const string ShortcutExtension = ".lnk";

    private static readonly Dictionary<string, SongKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mid"] = SongKind.Midi,
        [".rmi"] = SongKind.Midi,
        [".mp3"] = SongKind.Mp3,
        [".wav"] = SongKind.Wav,
        [".wma"] = SongKind.Wma,
        [".aif"] = SongKind.Aiff,
        [".aifc"] = SongKind.Aiff,
        [".aiff"] = SongKind.Aiff,
        [".au"] = SongKind.Au,
        [".snd"] = SongKind.Au,
        [".mpeg"] = SongKind.Mpeg,
        [".m4a"] = SongKind.M4a,
    };

    /// <summary>The type of a music file from its extension.</summary>
    /// <param name="path">A file name or path.</param>
    /// <param name="kind">The type.</param>
    /// <returns>True for the 12 music extensions of the "Music Files" filter.</returns>
    public static bool TryGetKind(string path, out SongKind kind) => Kinds.TryGetValue(Path.GetExtension(path), out kind);

    /// <summary>True for <c>.lnk</c> files.</summary>
    /// <param name="path">A file name or path.</param>
    /// <returns>True for shortcuts.</returns>
    public static bool IsShortcut(string path) => Path.GetExtension(path).Equals(ShortcutExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The 5.4 sort key: the file name upper-cased with the C-locale <c>toupper</c> (only a-z change), compared ordinally,
    /// so [ \ ] ^ _ and the backquote sort after the letters.
    /// </summary>
    /// <param name="fileName">The file name with extension.</param>
    /// <returns>The key.</returns>
    public static string SortKey(string fileName) => string.Create(fileName.Length, fileName, static (key, name) =>
    {
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            key[i] = c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;
        }
    });
}
