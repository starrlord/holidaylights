using System.Globalization;
using System.IO;

namespace HolidayLights.App.Settings;

/// <summary>What files dropped on the Settings window become (PRODUCT-SPEC 3.2.7): bulbs, songs, pictures, or nothing.</summary>
public static class DroppedFiles
{
    /// <summary>The InfoBar for any other file.</summary>
    public const string UnsupportedText = "Holiday Lights can add bulb files (.bul), animated GIF pictures (.gif), songs and pictures.";

    /// <summary>The 12 extensions of the "Music Files" filter (PRODUCT-SPEC 3.4.3).</summary>
    public static IReadOnlySet<string> SongExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mid", ".rmi", ".mp3", ".wav", ".wma", ".aif", ".aifc", ".aiff", ".au", ".snd", ".mpeg", ".m4a",
    };

    /// <summary>The "Picture Files" extensions other than <c>.gif</c>, which becomes a bulb when dropped (PRODUCT-SPEC 3.5.6, 3.2.7).</summary>
    public static IReadOnlySet<string> PictureExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".jpg", ".jpeg", ".png",
    };

    /// <summary>True for <c>.bul</c> and <c>.gif</c> files (imported as bulbs).</summary>
    /// <param name="path">A path.</param>
    /// <returns>True for bulb files and GIFs.</returns>
    public static bool IsBulbFile(string path) =>
        Path.GetExtension(path) is { } extension
        && (extension.Equals(".bul", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase));

    /// <summary>True for a GIF.</summary>
    /// <param name="path">A path.</param>
    /// <returns>True for <c>.gif</c>.</returns>
    public static bool IsGif(string path) => Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a music file.</summary>
    /// <param name="path">A path.</param>
    /// <returns>True for the music extensions.</returns>
    public static bool IsSong(string path) => SongExtensions.Contains(Path.GetExtension(path));

    /// <summary>True for a picture (other than a GIF).</summary>
    /// <param name="path">A path.</param>
    /// <returns>True for the picture extensions.</returns>
    public static bool IsPicture(string path) => PictureExtensions.Contains(Path.GetExtension(path));

    /// <summary>True when some of the files can be added: bulb files, GIFs, songs or pictures.</summary>
    /// <param name="paths">The files.</param>
    /// <returns>True when at least one is supported.</returns>
    public static bool CanAddAny(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Any(p => IsBulbFile(p) || IsSong(p) || IsPicture(p));
    }

    /// <summary>"Added 2 songs." / "Added 1 picture.".</summary>
    /// <param name="count">How many were added.</param>
    /// <param name="singular">"song" or "picture".</param>
    /// <returns>The snackbar sentence.</returns>
    public static string Added(int count, string singular) =>
        count == 1 ? $"Added 1 {singular}." : string.Create(CultureInfo.CurrentCulture, $"Added {count} {singular}s.");
}

/// <summary>The sentences after adding bulb files and GIFs (PRODUCT-SPEC 3.2.10, Appendix D).</summary>
public static class BulbImportTexts
{
    /// <summary>The InfoBar of a damaged bulb file (5.4 wording).</summary>
    public const string DamagedText = "Problem Importing File: Sorry, that bulb file is damaged and can't be used.";

    /// <summary>The InfoBar of an undecodable GIF (5.4 wording).</summary>
    public const string GifText = "Cannot Import GIF File: " + Core.Bulbs.BulbCatalog.GifCannotBeImportedText;

    /// <summary>The InfoBar of another file type (5.4 wording).</summary>
    public const string UnsupportedText = "Problem Importing File: " + Core.Bulbs.BulbCatalog.UnsupportedFileText;

    /// <summary>The tip after one new bulb.</summary>
    public const string NewBulbTip = "Double-click it to use it, or drag it into a box.";

    /// <summary>"&lt;name&gt; is already in your bulbs.".</summary>
    /// <param name="name">The bulb's name.</param>
    /// <returns>The InfoBar sentence.</returns>
    public static string AlreadyPresent(string name) => $"{name} is already in your bulbs.";

    /// <summary>"Couldn't copy "Star.bul": Access is denied.".</summary>
    /// <param name="path">The file.</param>
    /// <param name="reason">The Windows reason.</param>
    /// <returns>The InfoBar sentence.</returns>
    public static string CopyFailed(string path, string? reason) =>
        $"Couldn't copy \"{Path.GetFileName(path)}\": {(string.IsNullOrWhiteSpace(reason) ? "unknown error." : reason.Trim())}";

    /// <summary>
    /// The summary snackbar of several files: "Added 3 bulbs.", "Added 3 bulbs. 1 file was already in your bulbs.", or null
    /// when nothing was added.
    /// </summary>
    /// <param name="added">Bulbs added.</param>
    /// <param name="already">Files whose bulb was already present.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? Summary(int added, int already)
    {
        if (added == 0)
        {
            return null;
        }

        string text = added == 1 ? "Added 1 bulb." : string.Create(CultureInfo.CurrentCulture, $"Added {added} bulbs.");
        return already switch
        {
            0 => text,
            1 => text + " 1 file was already in your bulbs.",
            _ => text + string.Create(CultureInfo.CurrentCulture, $" {already} files were already in your bulbs."),
        };
    }
}
