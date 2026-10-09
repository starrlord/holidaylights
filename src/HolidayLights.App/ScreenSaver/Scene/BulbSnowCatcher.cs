using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>
/// Where snow sticks on one display (5.4 <c>BulbWorld_HitTest</c> and the strips' <c>AddSnowPixel</c>): every DIP pixel
/// covered by an opaque pixel of a bulb's frame 0. The snow is white, or grey where the art is black (0x999999 on the top
/// and bottom strips and the corners, 0x7F7F7F on the side strips).
/// </summary>
internal sealed class BulbSnowCatcher : ISnowCatcher
{
    /// <summary>Snow on coloured art.</summary>
    public const uint White = 0xFFFFFFFF;

    /// <summary>Snow on black art on a top or bottom strip or a corner.</summary>
    public const uint GreyOnHorizontal = 0xFF999999;

    /// <summary>Snow on black art on a left or right strip.</summary>
    public const uint GreyOnVertical = 0xFF7F7F7F;

    private static readonly uint[] Colors = [0, White, GreyOnHorizontal, GreyOnVertical];

    private readonly SaverField field;
    private readonly byte[] cells;

    private BulbSnowCatcher(SaverField field, byte[] cells)
    {
        this.field = field;
        this.cells = cells;
    }

    /// <summary>Rasterizes frame 0 of every placed bulb into DIP cells; later placements cover earlier ones, as drawn.</summary>
    /// <param name="layout">The display's bulb layout.</param>
    /// <param name="display">The display (its bounds and scale map DIPs to the layout's pixels).</param>
    /// <param name="field">The simulated screen.</param>
    /// <param name="bulbs">Resolves the bulbs.</param>
    /// <returns>The catcher.</returns>
    public static BulbSnowCatcher Create(LightsLayout layout, DisplayInfo display, SaverField field, IBulbResolver bulbs)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(bulbs);
        byte[] cells = new byte[field.Width * field.Height];
        double scale = display.Scale;
        foreach (BulbPlacement placement in layout.Placements)
        {
            if (bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb))
            {
                byte grey = placement.Slot is CellSlot.Left or CellSlot.Right ? (byte)3 : (byte)2;
                Rasterize(cells, field, bulb.GetCell(placement.Slot, placement.Flavor, 0).Image,
                    placement.Bounds.Offset(-display.Bounds.Left, -display.Bounds.Top), scale, grey);
            }
        }

        return new BulbSnowCatcher(field, cells);
    }

    /// <inheritdoc />
    public bool TryCatch(int x, int y, out uint color)
    {
        color = 0;
        if ((uint)x >= (uint)field.Width || (uint)y >= (uint)field.Height)
        {
            return false;
        }

        color = Colors[cells[y * field.Width + x]];
        return color != 0;
    }

    private static void Rasterize(byte[] cells, SaverField field, Rgba32Image art, RectI bounds, double scale, byte grey)
    {
        if (bounds.IsEmpty || art.Width == 0 || art.Height == 0)
        {
            return;
        }

        int left = Math.Max(0, (int)Math.Floor(bounds.Left / scale));
        int top = Math.Max(0, (int)Math.Floor(bounds.Top / scale));
        int right = Math.Min(field.Width, (int)Math.Ceiling(bounds.Right / scale));
        int bottom = Math.Min(field.Height, (int)Math.Ceiling(bounds.Bottom / scale));
        for (int y = top; y < bottom; y++)
        {
            double centerY = (y + 0.5) * scale;
            if (centerY < bounds.Top || centerY >= bounds.Bottom)
            {
                continue;
            }

            int artY = Math.Min(art.Height - 1, (int)((centerY - bounds.Top) * art.Height / bounds.Height));
            for (int x = left; x < right; x++)
            {
                double centerX = (x + 0.5) * scale;
                if (centerX < bounds.Left || centerX >= bounds.Right)
                {
                    continue;
                }

                int artX = Math.Min(art.Width - 1, (int)((centerX - bounds.Left) * art.Width / bounds.Width));
                uint pixel = art.Pixels[artY * art.Width + artX];
                if (Bgra32.A(pixel) != 0)
                {
                    cells[y * field.Width + x] = (pixel & 0x00FFFFFF) == 0 ? grey : (byte)1;
                }
            }
        }
    }
}
