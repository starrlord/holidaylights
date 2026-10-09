using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Rendering;

/// <summary>An image and where its top-left pixel lands.</summary>
/// <param name="Image">The image.</param>
/// <param name="At">Its position in target pixels.</param>
internal sealed record PlacedImage(PremultipliedImage Image, PointI At);

/// <summary>
/// The background picture of a display drawn once into a single layer (PRODUCT-SPEC 6.2.2, 3.5.6), as 5.4 drew it once into
/// the background: only the part of each placed picture that falls inside the target is scaled, and every tile lands in the
/// same layer. A panorama or a 200-megapixel photo therefore costs no more than the screen it covers, the layer is never
/// larger than the target, and a small tiled pattern is one image rather than thousands of pieces.
/// </summary>
/// <remarks>Thread-safe (no shared state); the compositor must be thread-safe too when used from several threads.</remarks>
internal static class PictureLayer
{
    /// <summary>Draws the picture's rectangles that fall inside <paramref name="target"/> into one layer.</summary>
    /// <param name="picture">The picture and its layout (DIPs).</param>
    /// <param name="pixels">Where DIPs land in target pixels.</param>
    /// <param name="target">The target pixels to cover: the display, or the part of it a renderer draws.</param>
    /// <param name="style">The Look's pixel style (pixel-art pictures).</param>
    /// <param name="compositor">Draws the pieces into the layer.</param>
    /// <returns>The layer (the bounds of the visible pieces) and its position, or null when no part of the picture is inside.</returns>
    public static PlacedImage? Compose(SaverPicture picture, SaverPixels pixels, RectI target, SpriteStyle style, ICpuCompositor compositor)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(compositor);
        var pieces = new List<(RectI Placed, RectI Visible)>();
        RectI bounds = default;
        foreach (RectI rect in picture.Layout.Rectangles)
        {
            RectI placed = pixels.Snap(rect);
            RectI visible = placed.Intersect(target);
            if (!visible.IsEmpty)
            {
                pieces.Add((placed, visible));
                bounds = bounds.Union(visible);
            }
        }

        if (pieces.Count == 0)
        {
            return null;
        }

        var layer = new PremultipliedImage(bounds.Width, bounds.Height);
        long wholeLimit = (long)target.Width * target.Height;
        var bySize = new Dictionary<SizeI, PremultipliedImage>();
        foreach ((RectI placed, RectI visible) in pieces)
        {
            if ((long)placed.Width * placed.Height <= wholeLimit)
            {
                // No bigger than the target: scaled whole once per size (tiles share it) and clipped while drawing.
                if (!bySize.TryGetValue(placed.Size, out PremultipliedImage? image))
                {
                    image = PictureScaler.Scale(picture.Image, placed.Size, picture.IsPixelArt, style);
                    bySize[placed.Size] = image;
                }

                compositor.Draw(layer, image, placed.Left - bounds.Left, placed.Top - bounds.Top, 1f, CompositeMode.SourceOver);
            }
            else
            {
                PremultipliedImage part = PictureScaler.ScaleRegion(
                    picture.Image, placed.Size, visible.Offset(-placed.Left, -placed.Top), picture.IsPixelArt, style);
                compositor.Draw(layer, part, visible.Left - bounds.Left, visible.Top - bounds.Top, 1f, CompositeMode.SourceOver);
            }
        }

        return new PlacedImage(layer, bounds.TopLeft);
    }
}
