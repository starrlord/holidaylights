using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>
/// Composes review sheets: every rendered image at 100 % and enlarged (nearest neighbour, so single pixels stay
/// visible) on several backgrounds. The sheets are for looking at the art; they are not shipped.
/// </summary>
internal static class ReviewSheet
{
    /// <summary>The backgrounds each image is shown on: light and dark taskbars, a white window and a blue wallpaper.</summary>
    private static readonly (string Name, Color Color)[] Backgrounds =
    [
        ("light taskbar", BulbPalette.Hex("#F3F3F3")),
        ("dark taskbar", BulbPalette.Hex("#202020")),
        ("white", Colors.White),
        ("wallpaper blue", BulbPalette.Hex("#1F4E8C")),
    ];

    /// <summary>Builds a sheet with one row per image.</summary>
    /// <param name="title">A heading.</param>
    /// <param name="rows">Labelled images.</param>
    /// <param name="zoom">Enlargement of a second copy (e.g. 4), or 1 for none.</param>
    /// <returns>The sheet.</returns>
    public static PixelImage Build(string title, IReadOnlyList<(string Label, PixelImage Image)> rows, int zoom)
    {
        int maxSize = rows.Max(r => Math.Max(r.Image.Width, r.Image.Height));
        int cell = maxSize * (zoom > 1 ? 1 + zoom : 1) + 24;
        int labelWidth = 150;
        int rowHeight = maxSize * zoom + 16;
        int width = labelWidth + Backgrounds.Length * cell;
        int height = 40 + rows.Count * rowHeight;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#BDBDBD")), null, new RectangleGeometry(new Rect(0, 0, width, height))));
        group.Children.Add(TextArt.At(title, new Point(8, 8), 16, Colors.Black));
        for (int b = 0; b < Backgrounds.Length; b++)
        {
            group.Children.Add(TextArt.At(Backgrounds[b].Name, new Point(labelWidth + b * cell + 4, 26), 11, Colors.Black));
        }

        for (int r = 0; r < rows.Count; r++)
        {
            (string label, PixelImage image) = rows[r];
            double y = 40 + r * rowHeight;
            group.Children.Add(TextArt.At(label, new Point(8, y + 4), 12, Colors.Black));
            PixelImage? zoomed = zoom > 1 ? PixelArt.Nearest(image, zoom) : null;
            for (int b = 0; b < Backgrounds.Length; b++)
            {
                double x = labelWidth + b * cell;
                var backgroundRect = new Rect(x, y, cell - 8, rowHeight - 6);
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(Backgrounds[b].Color), null, new RectangleGeometry(backgroundRect)));
                group.Children.Add(Raster.AsDrawing(image, new Rect(x + 6, y + 6, image.Width, image.Height)));
                if (zoomed is not null)
                {
                    group.Children.Add(Raster.AsDrawing(zoomed, new Rect(x + image.Width + 14, y + 4, zoomed.Width, zoomed.Height)));
                }
            }
        }

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawDrawing(group);
        }

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return Raster.ToImage(target);
    }
}
