using HolidayLights.Tests.Rendering.RealCore;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>A capture of a screen rectangle (opaque BGRA, top-down) with its position on the virtual screen.</summary>
/// <param name="Area">The captured rectangle (physical pixels).</param>
/// <param name="Pixels">The pixels.</param>
internal sealed record ScreenShot(RectI Area, uint[] Pixels)
{
    /// <summary>Captures a rectangle.</summary>
    public static ScreenShot Take(RectI area) => new(area, DesktopCapture.Capture(area));

    /// <summary>The pixel at a virtual-screen position.</summary>
    public uint At(int x, int y) => Pixels[((y - Area.Top) * Area.Width) + (x - Area.Left)];

    /// <summary>True when the position lies in the capture.</summary>
    public bool Contains(int x, int y) => x >= Area.Left && x < Area.Right && y >= Area.Top && y < Area.Bottom;
}

/// <summary>
/// What the screen must show for a placement, derived from the Core alone (not from the presenter): the sprite the Core
/// sprite pipeline makes for the frame, at the layout's placement, aligned the 5.4 way.
/// </summary>
internal static class ScreenExpectations
{
    /// <summary>Channel tolerance for opaque sprite pixels (DWM passes them through unchanged in SDR).</summary>
    public const int OpaqueTolerance = 2;

    /// <summary>The sprite of one frame of a placement (in the scene's pixel style) and where its top-left lies.</summary>
    public static (PremultipliedImage Image, PointI Position) Sprite(RealLights lights, LightsScene scene, BulbPlacement placement, int frame)
    {
        Assert.True(lights.Catalog.TryGetBulb(placement.BulbId, out IBulb? bulb));
        double scale = scene.Layout.Displays.Single(d => d.Target.DisplayId == placement.DisplayId).Target.Scale;
        PremultipliedImage image = lights.Sprites.GetSprite(bulb!, placement.Slot, placement.Flavor, frame, scale, scene.Effects.Pixels);
        return (image, Align(placement, image.Width, image.Height));
    }

    /// <summary>
    /// 5.4 alignment: top cells and the top corners at the strip top, bottom cells and corners at the strip bottom, right cells
    /// and the right corners at the right edge, everything else at the left edge of the placement.
    /// </summary>
    public static PointI Align(BulbPlacement placement, int width, int height)
    {
        bool right = placement.Slot is CellSlot.Right or CellSlot.TopRight or CellSlot.BottomRight;
        bool bottom = placement.Slot is CellSlot.Bottom or CellSlot.BottomLeft or CellSlot.BottomRight;
        return new PointI(right ? placement.Bounds.Right - width : placement.Bounds.Left, bottom ? placement.Bounds.Bottom - height : placement.Bounds.Top);
    }

    /// <summary>The frame a bulb shows in a state (light bulbs: lit when bright, else unlit).</summary>
    public static int ShownFrame(FlashBulbInfo bulb, BulbVisualState state) =>
        bulb.Kind == BulbAnimationKind.LightBulb ? (state.Brightness >= 0.5f ? bulb.LitFrame : 1 - bulb.LitFrame) : state.Frame;

    /// <summary>Counts the opaque pixels of a sprite and those the capture shows exactly.</summary>
    public static (int Opaque, int Matching) Compare(ScreenShot shot, PremultipliedImage image, PointI position)
    {
        int opaque = 0;
        int matching = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                uint pixel = image.Pixels[(y * image.Width) + x];
                if (pixel >> 24 != 0xFF || !shot.Contains(position.X + x, position.Y + y))
                {
                    continue;
                }

                opaque++;
                if (Distance(pixel, shot.At(position.X + x, position.Y + y)) <= OpaqueTolerance)
                {
                    matching++;
                }
            }
        }

        return (opaque, matching);
    }

    /// <summary>
    /// The brightness b a light bulb shows: the least-squares fit of <c>unlit + b (lit - unlit)</c> over the pixels where both
    /// frames are opaque and differ (the lit visual over the unlit one at opacity b).
    /// </summary>
    public static double FitBrightness(ScreenShot shot, PremultipliedImage lit, PremultipliedImage unlit, PointI position)
    {
        double numerator = 0;
        double denominator = 0;
        for (int y = 0; y < lit.Height && y < unlit.Height; y++)
        {
            for (int x = 0; x < lit.Width && x < unlit.Width; x++)
            {
                uint l = lit.Pixels[(y * lit.Width) + x];
                uint u = unlit.Pixels[(y * unlit.Width) + x];
                if (l >> 24 != 0xFF || u >> 24 != 0xFF || !shot.Contains(position.X + x, position.Y + y))
                {
                    continue;
                }

                uint s = shot.At(position.X + x, position.Y + y);
                for (int shift = 0; shift <= 16; shift += 8)
                {
                    double delta = (int)((l >> shift) & 0xFF) - (int)((u >> shift) & 0xFF);
                    double seen = (int)((s >> shift) & 0xFF) - (int)((u >> shift) & 0xFF);
                    numerator += delta * seen;
                    denominator += delta * delta;
                }
            }
        }

        return denominator > 0 ? numerator / denominator : double.NaN;
    }

    /// <summary>The frame whose opaque pixels the capture matches best (mean channel difference), and that difference.</summary>
    public static (int Frame, double Difference) BestFrame(ScreenShot shot, IReadOnlyList<(PremultipliedImage Image, PointI Position)> frames)
    {
        int best = -1;
        double bestDifference = double.MaxValue;
        for (int frame = 0; frame < frames.Count; frame++)
        {
            (PremultipliedImage image, PointI position) = frames[frame];
            double sum = 0;
            int count = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    uint pixel = image.Pixels[(y * image.Width) + x];
                    if (pixel >> 24 == 0xFF && shot.Contains(position.X + x, position.Y + y))
                    {
                        sum += Distance(pixel, shot.At(position.X + x, position.Y + y));
                        count++;
                    }
                }
            }

            double difference = count > 0 ? sum / count : double.MaxValue;
            if (difference < bestDifference)
            {
                best = frame;
                bestDifference = difference;
            }
        }

        return (best, bestDifference);
    }

    /// <summary>The largest channel difference of two pixels (alpha ignored).</summary>
    public static int Distance(uint a, uint b) =>
        Math.Max(Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF)),
            Math.Max(Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF)), Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF))));
}
