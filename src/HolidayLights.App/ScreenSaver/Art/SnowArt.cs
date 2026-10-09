using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Imaging;

namespace HolidayLights.App.ScreenSaver.Art;

/// <summary>
/// The flake pictures of the snow modules for sizes 2-5 (5.4 <c>SnowFlakeModule_DrawFlake</c>): a white 2 x 2 square for
/// size 2; for sizes 3-5 GDI's white ellipses (Snow) or the first three 9 x 9 FLAKE bitmaps (Snow Flakes).
/// </summary>
internal sealed class SnowArt
{
    private const uint White = 0xFFFFFFFF;

    /// <summary>
    /// <c>Ellipse(x, y, x + size, y + size)</c> with a white pen and brush, as GDI rasterizes it (captured on Windows 11):
    /// a plus for 3, a 4 x 4 and a 5 x 5 disc without their corner pixels.
    /// </summary>
    private static readonly string[][] EllipseMasks =
    [
        [".#.", "###", ".#."],
        [".##.", "####", "####", ".##."],
        [".###.", "#####", "#####", "#####", ".###."],
    ];

    private static readonly Lazy<SnowArt> ShapesArt = new(CreateShapes);
    private static readonly Lazy<SnowArt> FlakesArt = new(CreateFlakes);

    private readonly SpriteArt[] bySize;

    private SnowArt(SpriteArt[] bySize) => this.bySize = bySize;

    /// <summary>"Snow": squares and GDI ellipses.</summary>
    public static SnowArt Shapes => ShapesArt.Value;

    /// <summary>"Snow Flakes": a square for size 2, the FLAKE bitmaps 0-2 for sizes 3-5 (flakes 3 and 4 are never used, as in 5.4).</summary>
    public static SnowArt Flakes => FlakesArt.Value;

    /// <summary>The tallest picture (DIPs).</summary>
    public int MaxHeight => bySize.Max(a => a.Size.Height);

    /// <summary>The picture of a flake size.</summary>
    /// <param name="size">2 to 5.</param>
    /// <returns>The picture.</returns>
    public SpriteArt ForSize(int size) => bySize[Math.Clamp(size, 2, 5) - 2];

    private static SnowArt CreateShapes() =>
        new([Square(), .. EllipseMasks.Select((mask, i) => new SpriteArt($"snow:ellipse{i + 3}", [FromMask(mask)]))]);

    private static SnowArt CreateFlakes()
    {
        Rgba32Image strip = HeritageArt.Flakes;
        int size = strip.Height;
        return new(
        [
            Square(),
            .. Enumerable.Range(0, 3).Select(i => new SpriteArt($"snow:flake{i}", [strip.Crop(RectI.FromXYWH(i * size, 0, size, size))])),
        ]);
    }

    private static SpriteArt Square() => new("snow:square2", [FromMask(["##", "##"])]);

    private static Rgba32Image FromMask(string[] rows)
    {
        var image = new Rgba32Image(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] == '#')
                {
                    image[x, y] = White;
                }
            }
        }

        return image;
    }
}
