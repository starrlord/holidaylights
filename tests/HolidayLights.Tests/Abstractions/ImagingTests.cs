namespace HolidayLights.Tests.Abstractions;

public sealed class ImagingTests
{
    [Fact]
    public void RectI_HasExclusiveRightAndBottom()
    {
        var rect = new RectI(-3840, 0, 0, 2088);

        Assert.Equal(3840, rect.Width);
        Assert.Equal(2088, rect.Height);
        Assert.True(rect.Contains(new PointI(-3840, 0)));
        Assert.False(rect.Contains(new PointI(0, 0)));
        Assert.Equal("-3840,0,0,2088", rect.ToString());
    }

    [Fact]
    public void RectI_IntersectUnionOffset()
    {
        var a = RectI.FromXYWH(0, 0, 100, 50);
        var b = RectI.FromXYWH(50, 25, 100, 50);

        Assert.Equal(new RectI(50, 25, 100, 50), a.Intersect(b));
        Assert.Equal(new RectI(0, 0, 150, 75), a.Union(b));
        Assert.Equal(default, a.Intersect(RectI.FromXYWH(200, 200, 10, 10)));
        Assert.Equal(new RectI(10, 20, 110, 70), a.Offset(10, 20));
        Assert.True(a.IntersectsWith(b));
        Assert.True(a.Contains(RectI.FromXYWH(10, 10, 5, 5)));
    }

    [Fact]
    public void Bgra32_PacksChannels()
    {
        uint pixel = Bgra32.Pack(r: 0x11, g: 0x22, b: 0x33, a: 0x44);

        Assert.Equal(0x44112233u, pixel);
        Assert.Equal((byte)0x11, Bgra32.R(pixel));
        Assert.Equal((byte)0x22, Bgra32.G(pixel));
        Assert.Equal((byte)0x33, Bgra32.B(pixel));
        Assert.Equal((byte)0x44, Bgra32.A(pixel));
    }

    [Theory]
    [InlineData(255, 200, 100, 255, 255, 200, 100)]
    [InlineData(255, 200, 100, 0, 0, 0, 0)]
    [InlineData(255, 255, 255, 128, 128, 128, 128)]
    [InlineData(10, 20, 30, 51, 2, 4, 6)]
    public void Bgra32_PremultipliesWithRounding(byte r, byte g, byte b, byte a, byte pr, byte pg, byte pb)
    {
        uint result = Bgra32.Premultiply(Bgra32.Pack(r, g, b, a));

        Assert.Equal(Bgra32.Pack(pr, pg, pb, a), result);
    }

    [Fact]
    public void Bgra32_UnpremultiplyTreatsAdditiveLightAsTransparent()
    {
        Assert.Equal(Bgra32.Transparent, Bgra32.Unpremultiply(Bgra32.Pack(40, 30, 20, 0)));
        Assert.Equal(Bgra32.Pack(255, 255, 255, 128), Bgra32.Unpremultiply(Bgra32.Pack(128, 128, 128, 128)));
    }

    [Fact]
    public void RgbColor_ParsesAndFormats()
    {
        RgbColor color = RgbColor.Parse("#f4b942");

        Assert.Equal(new RgbColor(0xF4, 0xB9, 0x42), color);
        Assert.Equal("#F4B942", color.ToString());
        Assert.False(RgbColor.TryParse("F4B942", out _));
        Assert.False(RgbColor.TryParse("#F4B9", out _));
        Assert.Throws<FormatException>(() => RgbColor.Parse("red"));
    }

    [Fact]
    public void RgbColor_ConvertsColorRef()
    {
        // 5.4 stored the default message colour as COLORREF 0x000000FF (red).
        Assert.Equal(RgbColor.Red, RgbColor.FromColorRef(0x000000FF));
        Assert.Equal(0x00808000u, new RgbColor(0x00, 0x80, 0x80).ToColorRef());
    }

    [Fact]
    public void Rgba32Image_CropsAndPremultiplies()
    {
        var image = new Rgba32Image(3, 2);
        image[2, 1] = Bgra32.Pack(255, 255, 255, 128);

        Rgba32Image crop = image.Crop(new RectI(1, 1, 3, 2));
        PremultipliedImage premultiplied = image.ToPremultiplied();

        Assert.Equal(new SizeI(2, 1), crop.Size);
        Assert.Equal(Bgra32.Pack(255, 255, 255, 128), crop[1, 0]);
        Assert.Equal(Bgra32.Pack(128, 128, 128, 128), premultiplied[2, 1]);
        Assert.Throws<ArgumentException>(() => new Rgba32Image(2, 2, new uint[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba32Image(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Crop(new RectI(0, 0, 4, 1)));
    }
}
