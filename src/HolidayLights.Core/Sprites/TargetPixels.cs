namespace HolidayLights.Core.Sprites;

/// <summary>Maps physical (layout) pixels to the pixels of a preview target: <c>target = layout x zoom + offset</c>.</summary>
internal static class TargetPixels
{
    /// <summary>Maps one coordinate to the nearest target pixel; halves round up, so the mapping commutes with whole-pixel moves.</summary>
    /// <param name="value">Layout pixels.</param>
    /// <param name="zoom">Target pixels per layout pixel.</param>
    /// <param name="offset">Target pixels added after zooming.</param>
    /// <returns><c>floor(value x zoom + offset + 0.5)</c>.</returns>
    public static int Snap(int value, double zoom, double offset) => (int)Math.Floor(value * zoom + offset + 0.5);

    /// <summary>Maps a rectangle edge by edge with <see cref="Snap"/>.</summary>
    /// <param name="rect">Layout pixels.</param>
    /// <param name="zoom">Target pixels per layout pixel.</param>
    /// <param name="offsetX">Horizontal offset in target pixels.</param>
    /// <param name="offsetY">Vertical offset in target pixels.</param>
    /// <returns>The rectangle in target pixels.</returns>
    public static RectI Map(RectI rect, double zoom, double offsetX, double offsetY) => new(
        Snap(rect.Left, zoom, offsetX), Snap(rect.Top, zoom, offsetY), Snap(rect.Right, zoom, offsetX), Snap(rect.Bottom, zoom, offsetY));
}
