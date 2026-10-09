namespace HolidayLights.Core.Sprites;

/// <summary>The stage colours of PRODUCT-SPEC 4.2 that previews paint behind the lights.</summary>
public static class PreviewPalette
{
    /// <summary>Opacity of the taskbar band: 85 % of 255, rounded.</summary>
    private const byte TaskbarBandAlpha = 217;

    /// <summary>Top colour of the night gradient (#0B1530).</summary>
    public static RgbColor NightGradientTop => new(0x0B, 0x15, 0x30);

    /// <summary>Bottom colour of the night gradient (#1D3466).</summary>
    public static RgbColor NightGradientBottom => new(0x1D, 0x34, 0x66);

    /// <summary>The taskbar band, #202020 at 85 %, as a premultiplied pixel (<c>0xD91B1B1B</c>).</summary>
    public static uint TaskbarBand => Bgra32.Premultiply(new RgbColor(0x20, 0x20, 0x20).ToBgra32(TaskbarBandAlpha));

    /// <summary>Returns one row of the vertical night gradient.</summary>
    /// <param name="row">The row, 0 at the top.</param>
    /// <param name="height">The gradient's height in rows.</param>
    /// <returns>An opaque pixel: the linear blend from <see cref="NightGradientTop"/> to <see cref="NightGradientBottom"/>.</returns>
    public static uint NightGradientAt(int row, int height)
    {
        double t = height > 1 ? Math.Clamp(row / (height - 1.0), 0, 1) : 0;
        RgbColor top = NightGradientTop;
        RgbColor bottom = NightGradientBottom;
        return Bgra32.Pack(Blend(top.R, bottom.R, t), Blend(top.G, bottom.G, t), Blend(top.B, bottom.B, t), 255);
    }

    private static byte Blend(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
}
