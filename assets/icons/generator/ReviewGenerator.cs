using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>Writes review sheets of the generated art (icons at 100 % and 400 %, banners, help pictures).</summary>
internal static class ReviewGenerator
{
    /// <summary>Writes the sheets.</summary>
    /// <param name="paths">Repository locations.</param>
    /// <param name="folder">The output folder (outside the repository).</param>
    public static void Run(RepoPaths paths, string folder)
    {
        Directory.CreateDirectory(folder);
        WriteIconSheets(folder, "app", "Application icon", IconSet.AppSizes, IconSet.App);
        WriteIconSheets(folder, "document", "Document icon (.bul)", IconSet.DocumentSizes, IconSet.Document);
        var tray = new List<(string, PixelImage)>();
        foreach ((string file, bool lit, bool dark) in IconGenerator.TrayIcons)
        {
            tray.AddRange(IconSet.TraySizes.Select(s => ($"{Path.GetFileNameWithoutExtension(file)} {s}", IconGenerator.Render(size => IconSet.Tray(size, lit, dark), s))));
        }

        Raster.SavePng(ReviewSheet.Build("Tray icons", tray, 4), Path.Combine(folder, "icons-tray.png"));

        // The illustrations as the app loads them (the written XAML), at 100 % and at 300 % (vector, not enlarged pixels).
        ResourceDictionary illustrations = IllustrationGenerator.Load(Path.Combine(paths.AppAssets, "Illustrations.xaml"));
        foreach ((string key, _, _) in IllustrationGenerator.Illustrations)
        {
            var image = (DrawingImage)illustrations[key];
            int width = (int)Math.Ceiling(image.Width);
            int height = (int)Math.Ceiling(image.Height);
            var rows = new List<(string, PixelImage)>
            {
                ("100 %", Raster.RenderImage(image.Drawing, width, height)),
                ("300 %", Raster.RenderImage(image.Drawing, width * 3, height * 3, 3)),
            };
            Raster.SavePng(ReviewSheet.Build(key, rows, 1), Path.Combine(folder, $"illustration-{key["HL.Illustration.".Length..]}.png"));
        }

        // The banners as the app shows them: the 4x files scaled down by area averaging to 100 %, 150 % and 200 %.
        string banners = Path.Combine(paths.AppAssets, "Banner");
        foreach (string file in new[] { "AboutBanner.png", "AboutFlash1.png", "AboutFlash2.png", "HelpBanner.png" })
        {
            PixelImage art = Raster.Load(Path.Combine(banners, file));
            int width = art.Width / BannerGenerator.Scale;
            int height = art.Height / BannerGenerator.Scale;
            var rows = new List<(string, PixelImage)>
            {
                ("100 %", Downscale(art, width, height)),
                ("150 %", Downscale(art, width * 3 / 2, height * 3 / 2)),
                ("200 %", Downscale(art, width * 2, height * 2)),
            };
            Raster.SavePng(ReviewSheet.Build(file, rows, 1), Path.Combine(folder, $"banner-{Path.GetFileNameWithoutExtension(file)}.png"));
        }

        Console.WriteLine($"Review sheets written to {folder}.");
    }

    /// <summary>Scales an image down the way WPF shows it with <see cref="BitmapScalingMode.Fant"/>.</summary>
    private static PixelImage Downscale(PixelImage image, int width, int height)
    {
        var drawing = new ImageDrawing(image.ToBitmapSource(), new Rect(0, 0, width, height));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawDrawing(drawing);
        }

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return Raster.ToImage(target);
    }

    private static void WriteIconSheets(string folder, string name, string title, IReadOnlyList<int> sizes, Func<int, Drawing> art)
    {
        Raster.SavePng(
            ReviewSheet.Build($"{title} - small sizes at 100 % and 400 %", [.. sizes.Where(s => s <= 64).Select(s => ($"{s} px", IconGenerator.Render(art, s)))], 4),
            Path.Combine(folder, $"icons-{name}-small.png"));
        Raster.SavePng(
            ReviewSheet.Build($"{title} - 256 px", [.. sizes.Where(s => s > 64).Select(s => ($"{s} px", IconGenerator.Render(art, s)))], 1),
            Path.Combine(folder, $"icons-{name}-256.png"));
    }
}
