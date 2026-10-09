using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>
/// Writes the 2003 banner art, enlarged 4x with MMPX, into <c>src/HolidayLights.App/Assets/Banner</c> (PRODUCT-SPEC 3.9,
/// 3.10, 3.11, 4.3.5): shown at 1x-4x, WPF's area-averaging downscale (<c>BitmapScalingMode.Fant</c>) completes the
/// "MMPX + area averaging" rule at every display scale.
/// <list type="bullet">
/// <item><c>AboutBanner.png</c> - the ABOUT banner with "Modern Edition 6.0" where 5.4 printed "version 5.4".</item>
/// <item><c>AboutFlash1.png</c>, <c>AboutFlash2.png</c> - the two frames of the light strip (alternate every 500 ms).</item>
/// <item><c>HelpBanner.png</c> - the 5.4 help banner (<c>bm0</c>) shown above every Help topic.</item>
/// </list>
/// The heritage sky (#DDEEFF) stays in light and dark mode: it is artwork (PRODUCT-SPEC 3.9).
/// </summary>
internal static class BannerGenerator
{
    /// <summary>The enlargement of every banner file.</summary>
    public const int Scale = 4;

    /// <summary>The edition line drawn on the About banner.</summary>
    private const string EditionText = "Modern Edition 6.0";

    /// <summary>The navy of the 2003 wordmark (#000096), used for the edition line.</summary>
    private static readonly Color WordmarkBlue = BulbPalette.Hex("#000096");

    /// <summary>Left edge and baseline of the edition line on the 480 x 94 banner: right of the "g" descender, under "Lights".</summary>
    private static readonly Point EditionOrigin = new(336, 82);

    private const double EditionFontSize = 10.5;

    /// <summary>Writes the banner files.</summary>
    /// <param name="paths">Repository locations.</param>
    public static void Run(RepoPaths paths)
    {
        string folder = Path.Combine(paths.AppAssets, "Banner");
        string bitmaps = Path.Combine(paths.Heritage, "bitmaps");
        Raster.SavePng(AboutBanner(Path.Combine(bitmaps, "ABOUT.bmp")), Path.Combine(folder, "AboutBanner.png"));
        Raster.SavePng(LightStrip(Path.Combine(bitmaps, "ABOUTFLASH1.bmp")), Path.Combine(folder, "AboutFlash1.png"));
        Raster.SavePng(LightStrip(Path.Combine(bitmaps, "ABOUTFLASH2.bmp")), Path.Combine(folder, "AboutFlash2.png"));
        Raster.SavePng(Mmpx.Scale4x(Raster.Load(Path.Combine(paths.Heritage, "help", "bm0.png"))), Path.Combine(folder, "HelpBanner.png"));
        Console.WriteLine($"Banners written to {folder}.");
    }

    /// <summary>The ABOUT banner enlarged, with the edition line.</summary>
    /// <param name="aboutBmp">The heritage ABOUT.bmp.</param>
    /// <returns>The image (1920 x 376).</returns>
    private static PixelImage AboutBanner(string aboutBmp)
    {
        PixelImage banner = Mmpx.Scale4x(Raster.Load(aboutBmp));
        Drawing lettering = TextArt.Line(
            EditionText, new Point(EditionOrigin.X * Scale, EditionOrigin.Y * Scale), EditionFontSize * Scale, WordmarkBlue, FontWeights.SemiBold);
        Raster.Over(banner, Raster.RenderImage(lettering, banner.Width, banner.Height), 0, 0);
        return banner;
    }

    /// <summary>
    /// A light strip frame enlarged. It keeps its sky: the 2003 bulbs have edge pixels blended into #DDEEFF, so the strip
    /// belongs on the heritage sky panel above the banner, where 5.4 drew it.
    /// </summary>
    /// <param name="flashBmp">ABOUTFLASH1.bmp or ABOUTFLASH2.bmp.</param>
    /// <returns>The image (1184 x 60).</returns>
    private static PixelImage LightStrip(string flashBmp) => Mmpx.Scale4x(Raster.Load(flashBmp));
}
