using System.Collections.Concurrent;

namespace HolidayLights.Tests.Rendering.Fakes;

/// <summary>
/// Stand-in for core-sprites: nearest-neighbour scaling to <see cref="ArtScale"/> sizes and a Gaussian glow halo of the
/// emissive pixels (luma(lit) - luma(unlit) &gt;= 32), premultiplied with alpha 0 as PRODUCT-SPEC 5.9 describes.
/// </summary>
internal sealed class FakeSpriteProvider : ISpriteProvider
{
    private readonly ConcurrentDictionary<SpriteKey, PremultipliedImage> sprites = new();
    private readonly ConcurrentDictionary<(string, CellSlot, int, double), GlowSprite?> glows = new();

    public int SpriteRequests;

    public PremultipliedImage GetSprite(IBulb bulb, CellSlot slot, int flavor, int frame, double scale, SpriteStyle style)
    {
        Interlocked.Increment(ref SpriteRequests);
        return sprites.GetOrAdd(new SpriteKey(bulb.ContentKey, slot, flavor, frame, scale, style), _ => Scale(bulb.GetCell(slot, flavor, frame).Image, scale).ToPremultiplied());
    }

    public GlowSprite? GetGlow(IBulb bulb, CellSlot slot, int flavor, double scale) =>
        glows.GetOrAdd((bulb.ContentKey, slot, flavor, scale), _ => MakeGlow(bulb, slot, flavor, scale));

    public Task PrefetchAsync(IEnumerable<SpriteRequest> requests, CancellationToken cancellationToken = default)
    {
        foreach (SpriteRequest request in requests)
        {
            GetSprite(request.Bulb, request.Slot, request.Flavor, request.Frame, request.Scale, request.Style);
        }

        return Task.CompletedTask;
    }

    public void Evict(string contentKey)
    {
    }

    internal static Rgba32Image Scale(Rgba32Image source, double scale)
    {
        SizeI size = ArtScale.Scale(source.Size, scale);
        var result = new Rgba32Image(size.Width, size.Height);
        for (int y = 0; y < size.Height; y++)
        {
            int sy = Math.Min(source.Height - 1, (int)(y * source.Height / (double)size.Height));
            for (int x = 0; x < size.Width; x++)
            {
                int sx = Math.Min(source.Width - 1, (int)(x * source.Width / (double)size.Width));
                result[x, y] = source[sx, sy];
            }
        }

        return result;
    }

    private static GlowSprite? MakeGlow(IBulb bulb, CellSlot slot, int flavor, double scale)
    {
        BulbAnimationInfo info = bulb.GetAnimation(slot, flavor);
        if (info.Kind != BulbAnimationKind.LightBulb)
        {
            return null;
        }

        Rgba32Image lit = Scale(bulb.GetCell(slot, flavor, info.LitFrame).Image, scale);
        Rgba32Image unlit = Scale(bulb.GetCell(slot, flavor, info.UnlitFrame).Image, scale);
        double sigma = 0.22 * Math.Max(lit.Width, lit.Height);
        int margin = (int)Math.Ceiling(3 * sigma);
        int width = lit.Width + 2 * margin;
        int height = lit.Height + 2 * margin;
        var r = new double[width * height];
        var g = new double[width * height];
        var b = new double[width * height];
        bool any = false;
        for (int y = 0; y < lit.Height; y++)
        {
            for (int x = 0; x < lit.Width; x++)
            {
                uint on = lit[x, y];
                uint off = unlit[x, y];
                if (Bgra32.A(on) == 0 || Luma(on) - Luma(off) < 32)
                {
                    continue;
                }

                int index = (y + margin) * width + x + margin;
                r[index] = Bgra32.R(on);
                g[index] = Bgra32.G(on);
                b[index] = Bgra32.B(on);
                any = true;
            }
        }

        if (!any)
        {
            return null;
        }

        double[] kernel = Kernel(sigma, margin);
        foreach (double[] channel in new[] { r, g, b })
        {
            Blur(channel, width, height, kernel, horizontal: true);
            Blur(channel, width, height, kernel, horizontal: false);
        }

        var pixels = new uint[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Bgra32.Pack(ToByte(r[i] * 2), ToByte(g[i] * 2), ToByte(b[i] * 2), 0);
        }

        return new GlowSprite(new PremultipliedImage(width, height, pixels), -margin, -margin);
    }

    private static double Luma(uint pixel) => 0.299 * Bgra32.R(pixel) + 0.587 * Bgra32.G(pixel) + 0.114 * Bgra32.B(pixel);

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static double[] Kernel(double sigma, int radius)
    {
        var kernel = new double[2 * radius + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            kernel[i + radius] = Math.Exp(-i * i / (2 * sigma * sigma));
            sum += kernel[i + radius];
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }

    private static void Blur(double[] channel, int width, int height, double[] kernel, bool horizontal)
    {
        int radius = kernel.Length / 2;
        var copy = (double[])channel.Clone();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = horizontal ? x + k : x;
                    int sy = horizontal ? y : y + k;
                    if (sx >= 0 && sx < width && sy >= 0 && sy < height)
                    {
                        sum += copy[sy * width + sx] * kernel[k + radius];
                    }
                }

                channel[y * width + x] = sum;
            }
        }
    }
}
