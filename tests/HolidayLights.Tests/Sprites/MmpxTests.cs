using HolidayLights.Core.Sprites;

namespace HolidayLights.Tests.Sprites;

/// <summary>
/// MMPX against the prototype's references (<c>Sprites/Golden/upscale</c>, made from the bulb sheet 1001/1002 with
/// samples outside the image read as transparent) and the clamp-to-edge output of the reference Python port
/// (golden <c>Golden/mmpx-2x-clamp.png</c>).
/// </summary>
public sealed class MmpxTests
{
    private static readonly Lazy<Rgba32Image> Sheet = new(() => TestImages.Load(TestImages.UpscaleResult("1x.png")));

    [Fact]
    public void Scale2x_TransparentEdge_MatchesThePocReferenceBitExactly()
    {
        Rgba32Image expected = TestImages.Load(TestImages.UpscaleResult("2x-mmpx.png"));

        Rgba32Image actual = Mmpx.Scale2x(Sheet.Value, MmpxEdge.Transparent);

        Assert.Equal(new SizeI(640, 272), actual.Size);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void Scale2x_TwiceWithTransparentEdge_MatchesThe4xPocReferenceBitExactly()
    {
        Rgba32Image expected = TestImages.Load(TestImages.UpscaleResult("4x-mmpx.png"));

        Rgba32Image actual = Mmpx.Scale2x(Mmpx.Scale2x(Sheet.Value, MmpxEdge.Transparent), MmpxEdge.Transparent);

        Assert.Equal(new SizeI(1280, 544), actual.Size);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void Scale2x_ClampEdge_MatchesTheReferencePythonPortBitExactly()
    {
        Rgba32Image expected = TestImages.Load(TestImages.SpritesGolden("mmpx-2x-clamp.png"));

        Rgba32Image actual = Mmpx.Scale2x(Sheet.Value);

        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void Scale2x_EdgeModesDifferOnlyWithinThreePixelsOfTheBorder()
    {
        Rgba32Image clamp = Mmpx.Scale2x(Sheet.Value, MmpxEdge.Clamp);
        Rgba32Image transparent = Mmpx.Scale2x(Sheet.Value, MmpxEdge.Transparent);
        int width = Sheet.Value.Width;
        int height = Sheet.Value.Height;

        int differences = 0;
        for (int y = 0; y < clamp.Height; y++)
        {
            for (int x = 0; x < clamp.Width; x++)
            {
                if (clamp[x, y] != transparent[x, y])
                {
                    differences++;
                    int distance = Math.Min(Math.Min(x / 2, y / 2), Math.Min(width - 1 - x / 2, height - 1 - y / 2));
                    Assert.True(distance < 3, $"({x},{y}) differs {distance} source pixels from the border.");
                }
            }
        }

        // The count the reference Python port gives for the same comparison.
        Assert.Equal(49, differences);
    }

    [Fact]
    public void Scale2x_UniformImageStaysUniform()
    {
        Rgba32Image actual = Mmpx.Scale2x(Art.Solid(5, 3, Art.Green));

        Assert.Equal(new SizeI(10, 6), actual.Size);
        Assert.All(actual.Pixels, p => Assert.Equal(Art.Green, p));
    }

    [Fact]
    public void Scale2x_KeepsThePaletteAndAddsNoColours()
    {
        Rgba32Image source = Sheet.Value;
        var palette = source.Pixels.ToHashSet();

        Rgba32Image actual = Mmpx.Scale2x(source);

        Assert.All(actual.Pixels.Distinct(), p => Assert.Contains(p, palette));
    }

    [Fact]
    public void Scale2x_TurnsAStaircaseIntoASmoothSlope()
    {
        // A 1:1 staircase between two colours becomes a straight 1:1 slope (expected output of the reference Python port).
        var source = new Rgba32Image(6, 6);
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                source[x, y] = x + y < 6 ? Art.Red : Art.Green;
            }
        }

        string[] expected =
        [
            "RRRRRRRRRRRR", "RRRRRRRRRRRR", "RRRRRRRRRRGG", "RRRRRRRRRGGG", "RRRRRRRRGGGG", "RRRRRRRGGGGG",
            "RRRRRRGGGGGG", "RRRRRGGGGGGG", "RRRRGGGGGGGG", "RRRGGGGGGGGG", "RRGGGGGGGGGG", "RRGGGGGGGGGG",
        ];

        Rgba32Image actual = Mmpx.Scale2x(source);

        string[] rows = [.. Enumerable.Range(0, 12).Select(y => string.Concat(
            Enumerable.Range(0, 12).Select(x => actual[x, y] == Art.Red ? 'R' : actual[x, y] == Art.Green ? 'G' : '?')))];
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void Scale2x_HandlesEmptyAndSinglePixelImages()
    {
        Assert.Equal(new SizeI(0, 0), Mmpx.Scale2x(new Rgba32Image(0, 0)).Size);
        Rgba32Image single = Mmpx.Scale2x(Art.Solid(1, 1, Art.Red));
        Assert.All(single.Pixels, p => Assert.Equal(Art.Red, p));
    }
}
