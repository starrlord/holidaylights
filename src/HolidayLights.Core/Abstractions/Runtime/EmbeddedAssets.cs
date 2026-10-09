using System.Reflection;

namespace HolidayLights.Core.Abstractions;

/// <summary>
/// Read access to the data embedded in <c>HolidayLights.Core</c>: <c>assets/builtin</c> (the built-in bulb table and art),
/// <c>assets/themes</c> (shipped themes) and <c>assets/heritage</c> (original 5.4 art shared by several owners).
/// </summary>
/// <remarks>Names use forward slashes relative to <c>assets/</c>, e.g. <c>builtin/table.json</c> or <c>heritage/bitmaps/ABOUT.bmp</c>.</remarks>
public static class EmbeddedAssets
{
    /// <summary>Prefix of the manifest resource names (see <c>HolidayLights.Core.csproj</c>).</summary>
    public const string ResourcePrefix = "holidaylights/";

    /// <summary>The built-in bulb table (<c>holidaylights.builtin-table/1</c>).</summary>
    public const string BuiltInTable = "builtin/table.json";

    private static readonly Assembly Assembly = typeof(EmbeddedAssets).Assembly;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Names = new(() =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(n => n[ResourcePrefix.Length..].Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase));

    /// <summary>The embedded RT_BITMAP file of a built-in bulb sheet or mask.</summary>
    /// <param name="bitmapId">An RT_BITMAP id from the table (e.g. 1001).</param>
    /// <returns>The asset name, e.g. <c>builtin/bmp/1001.bmp</c>.</returns>
    public static string BuiltInBitmap(int bitmapId) => $"builtin/bmp/{bitmapId}.bmp";

    /// <summary>True when an asset exists.</summary>
    /// <param name="name">The asset name.</param>
    /// <returns>True when embedded.</returns>
    public static bool Exists(string name) => Names.Value.ContainsKey(name);

    /// <summary>Lists the assets under a folder.</summary>
    /// <param name="folderPrefix">A prefix such as <c>themes/</c>.</param>
    /// <returns>Asset names, sorted ordinally.</returns>
    public static IReadOnlyList<string> List(string folderPrefix) =>
        Names.Value.Keys.Where(k => k.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Opens an asset.</summary>
    /// <param name="name">The asset name.</param>
    /// <returns>A read-only stream; dispose it.</returns>
    /// <exception cref="FileNotFoundException">The asset is not embedded.</exception>
    public static Stream Open(string name) =>
        Names.Value.TryGetValue(name, out string? resource) && Assembly.GetManifestResourceStream(resource) is { } stream
            ? stream
            : throw new FileNotFoundException($"Embedded asset '{name}' not found.", name);

    /// <summary>Reads an asset completely.</summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ReadAllBytes(string name)
    {
        using Stream stream = Open(name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>Names of the original 5.4 assets in <c>assets/heritage</c> (see its README for provenance).</summary>
public static class HeritageAssets
{
    /// <summary>The About banner, 480 x 94, 8 bpp.</summary>
    public const string About = "heritage/bitmaps/ABOUT.bmp";

    /// <summary>The light strip, frame 1 (296 x 15).</summary>
    public const string AboutFlash1 = "heritage/bitmaps/ABOUTFLASH1.bmp";

    /// <summary>The light strip, frame 2 (296 x 15).</summary>
    public const string AboutFlash2 = "heritage/bitmaps/ABOUTFLASH2.bmp";

    /// <summary>Damaged bulb art, 32 x 32 (with <see cref="WarningMask"/>).</summary>
    public const string Warning = "heritage/bitmaps/WARNING.bmp";

    /// <summary>Mask of <see cref="Warning"/> (black = opaque).</summary>
    public const string WarningMask = "heritage/bitmaps/WARNINGMASK.bmp";

    /// <summary>The 8 x 8 checkerboard that dims the outside of the Bulb Editing white square.</summary>
    public const string Dither = "heritage/bitmaps/DITHER.bmp";

    /// <summary>Snow Flakes module art: five 9 x 9 flakes (with <see cref="FlakeMask"/>).</summary>
    public const string Flake = "heritage/bitmaps/FLAKE.bmp";

    /// <summary>Mask of <see cref="Flake"/>.</summary>
    public const string FlakeMask = "heritage/bitmaps/FLAKEMASK.bmp";

    /// <summary>Balloons module art: six 21 x 50 balloons (with <see cref="BalloonMask"/>).</summary>
    public const string Balloon = "heritage/bitmaps/BALLOON.bmp";

    /// <summary>Mask of <see cref="Balloon"/>.</summary>
    public const string BalloonMask = "heritage/bitmaps/BALLOONMASK.bmp";

    /// <summary>Small balloons for the 5.4 preview: six 5 x 7 (with <see cref="BalloonSmallMask"/>).</summary>
    public const string BalloonSmall = "heritage/bitmaps/BALLOONSMALL.bmp";

    /// <summary>Mask of <see cref="BalloonSmall"/>.</summary>
    public const string BalloonSmallMask = "heritage/bitmaps/BALLOONMASKSMALL.bmp";

    /// <summary>The 21-entry screen saver animation table (JSON).</summary>
    public const string SaverAnimationTable = "heritage/saver/anim_table.json";

    /// <summary>The help banner (PNG).</summary>
    public const string HelpBanner = "heritage/help/bm0.png";

    /// <summary>The 5.4 application icon (reference art).</summary>
    public const string Icon54 = "heritage/icons/ICON.ico";

    /// <summary>The 5.4 <c>.bul</c> document icon (reference art).</summary>
    public const string DocumentIcon54 = "heritage/icons/2.ico";

    /// <summary>A screen saver GIF floater (RCDATA 3100-3105).</summary>
    /// <param name="resourceId">3100 Singing Tree, 3101 Dancing Demon, 3102 Skeleton, 3103 Gingerbread Man, 3104 Angel, 3105 Santa.</param>
    /// <returns>The asset name.</returns>
    public static string SaverGif(int resourceId) =>
        resourceId is >= 3100 and <= 3105
            ? $"heritage/saver/{resourceId}.gif"
            : throw new ArgumentOutOfRangeException(nameof(resourceId), resourceId, "Saver GIFs are 3100-3105.");
}
