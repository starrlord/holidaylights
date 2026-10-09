namespace HolidayLights.Tests.Bulbs;

/// <summary>Small GIFs used to build test bulbs.</summary>
internal static class TestBulbs
{
    /// <summary>A 2 x 2 red and transparent still.</summary>
    public static byte[] RedGif { get; } = Still((255, 0, 0));

    /// <summary>A 2 x 2 blue and transparent still.</summary>
    public static byte[] BlueGif { get; } = Still((0, 0, 255));

    /// <summary>A 2 x 2 green and transparent still.</summary>
    public static byte[] GreenGif { get; } = Still((0, 255, 0));

    private static byte[] Still((byte, byte, byte) color) =>
        TestGif.Encode(2, 2, [(0, 0, 0), color], [new TestGifFrame { Width = 2, Height = 2, Pixels = [1, 0, 0, 1], TransparentIndex = 0 }]);
}
