namespace HolidayLights.Tests.Sprites;

/// <summary>
/// A synthetic bulb: the same animation (frames, kind) in every slot, a configurable flavor count and content key, and a
/// count of decoded cells (to prove caching).
/// </summary>
internal sealed class FakeBulb : IBulb
{
    private readonly Rgba32Image[] frames;
    private readonly BulbAnimationKind kind;
    private readonly int litFrame;
    private int cellReads;

    public FakeBulb(string id, BulbAnimationKind kind, params Rgba32Image[] frames)
        : this(id, kind, litFrame: 0, flavorCount: 1, frames)
    {
    }

    public FakeBulb(string id, BulbAnimationKind kind, int litFrame, int flavorCount, params Rgba32Image[] frames)
    {
        Id = id;
        ContentKey = id + "|1";
        this.kind = kind;
        this.litFrame = litFrame;
        FlavorCount = flavorCount;
        this.frames = frames;
    }

    /// <summary>How often a cell was decoded.</summary>
    public int CellReads => Volatile.Read(ref cellReads);

    public int FlavorCount { get; }

    public string Id { get; }

    public int LegacyId => 1000;

    public BulbOrigin Origin => BulbOrigin.UserAddOn;

    public string ContentKey { get; init; }

    public string Name => "Fake " + Id;

    public string Description => "";

    public string Author => "";

    public string Copyright => "";

    public IReadOnlyList<string> Categories => [];

    public string? FilePath => null;

    public bool IsLocked => false;

    public bool IsEditable => true;

    public bool HasDamagedArt => false;

    public int HorizontalSpacing => 0;

    public int VerticalSpacing => 0;

    public RectI PreviewWindow => new(0, 0, 32, 32);

    public int GetFlavorCount(Side side) => FlavorCount;

    public int GetPhaseCount(Side side) => frames.Length;

    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor) => new(kind, frames.Length, litFrame, frames[0].Size);

    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => frames[phase % frames.Length].Size;

    public BulbCell GetCell(CellSlot slot, int flavor, int phase)
    {
        Interlocked.Increment(ref cellReads);
        return new BulbCell(frames[phase % frames.Length], null, false);
    }
}

/// <summary>Resolves a fixed set of bulbs.</summary>
internal sealed class FakeResolver(params IBulb[] bulbs) : IBulbResolver
{
    private readonly Dictionary<string, IBulb> byId = bulbs.ToDictionary(b => b.Id, BulbIds.Comparer);

    public bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb) =>
        byId.TryGetValue(id, out bulb);
}

/// <summary>Small synthetic pictures.</summary>
internal static class Art
{
    public const uint Red = 0xFFE3342F;
    public const uint DarkRed = 0xFF4A1010;
    public const uint Green = 0xFF1F8B4C;
    public const uint Black = 0xFF000000;

    /// <summary>A picture filled with one colour.</summary>
    public static Rgba32Image Solid(int width, int height, uint color)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, color);
        return new Rgba32Image(width, height, pixels);
    }

    /// <summary>A filled disc of <paramref name="color"/> centred in a transparent square.</summary>
    public static Rgba32Image Disc(int size, uint color, double radius)
    {
        var image = new Rgba32Image(size, size);
        double centre = (size - 1) / 2.0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if ((x - centre) * (x - centre) + (y - centre) * (y - centre) <= radius * radius)
                {
                    image[x, y] = color;
                }
            }
        }

        return image;
    }

    /// <summary>A lit/unlit pair: a bright disc with a black cord row, and the same shape dark.</summary>
    public static (Rgba32Image Lit, Rgba32Image Unlit) LightBulbPair(int size)
    {
        Rgba32Image lit = Disc(size, Red, size / 3.0);
        Rgba32Image unlit = Disc(size, DarkRed, size / 3.0);
        for (int x = 0; x < size; x++)
        {
            lit[x, 0] = Black;
            unlit[x, 0] = Black;
        }

        return (lit, unlit);
    }

    /// <summary>Deterministic pseudo-random pixels with every kind of alpha.</summary>
    public static uint[] Noise(int count, int seed, bool premultiplied)
    {
        var random = new Random(seed);
        var pixels = new uint[count];
        for (int i = 0; i < count; i++)
        {
            int kind = random.Next(6);
            uint a = kind switch { 0 => 0u, 1 => 255u, _ => (uint)random.Next(256) };
            uint limit = premultiplied && kind != 5 ? a : 255;
            uint r = (uint)random.Next((int)limit + 1);
            uint g = (uint)random.Next((int)limit + 1);
            uint b = (uint)random.Next((int)limit + 1);
            pixels[i] = a << 24 | r << 16 | g << 8 | b;
        }

        return pixels;
    }
}
