using System.IO;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>Writes the icon files named by <c>AppAssets</c> and their 256 px PNG masters.</summary>
internal static class IconGenerator
{
    /// <summary>The tray icon files: lit or unlit, light or dark taskbar.</summary>
    public static readonly (string File, bool Lit, bool Dark)[] TrayIcons =
    [
        ("TrayLitLight.ico", true, false),
        ("TrayLitDark.ico", true, true),
        ("TrayUnlitLight.ico", false, false),
        ("TrayUnlitDark.ico", false, true),
    ];

    /// <summary>Renders and writes every icon.</summary>
    /// <param name="paths">Repository locations.</param>
    public static void Run(RepoPaths paths)
    {
        Write(Path.Combine(paths.AppAssets, "HolidayLights.ico"), IconSet.AppSizes, IconSet.App);
        Write(Path.Combine(paths.AppAssets, "BulbDocument.ico"), IconSet.DocumentSizes, IconSet.Document);
        foreach ((string file, bool lit, bool dark) in TrayIcons)
        {
            Write(Path.Combine(paths.AppAssets, "Tray", file), IconSet.TraySizes, size => IconSet.Tray(size, lit, dark));
        }

        Raster.SavePng(Render(IconSet.App, 256), Path.Combine(paths.IconMasters, "app-256.png"));
        Raster.SavePng(Render(IconSet.Document, 256), Path.Combine(paths.IconMasters, "bul-document-256.png"));
        foreach ((string file, bool lit, bool dark) in TrayIcons)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            Raster.SavePng(Render(size => IconSet.Tray(size, lit, dark), 32), Path.Combine(paths.IconMasters, $"{name}-32.png"));
        }

        Console.WriteLine($"Icons written to {paths.AppAssets}.");
    }

    /// <summary>Renders one icon frame.</summary>
    /// <param name="art">The art for a size.</param>
    /// <param name="size">Pixel size.</param>
    /// <returns>The frame.</returns>
    public static PixelImage Render(Func<int, Drawing> art, int size) => Raster.RenderImage(art(size), size, size);

    private static void Write(string path, IReadOnlyList<int> sizes, Func<int, Drawing> art) =>
        IcoWriter.Write(path, [.. sizes.Select(size => Render(art, size))]);
}
