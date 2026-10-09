namespace HolidayLights.Tests.Rendering.Fakes;

/// <summary>A bulb with solid-colour frames of a given kind (unit tests that need no real art).</summary>
internal sealed class SyntheticBulb : IBulb
{
    private readonly BulbAnimationKind kind;
    private readonly int frames;
    private readonly SizeI size;

    public SyntheticBulb(string id, BulbAnimationKind kind, int frames, int size = 32, int flavors = 1)
    {
        Id = id;
        this.kind = kind;
        this.frames = frames;
        this.size = new SizeI(size, size);
        Flavors = flavors;
    }

    public string Id { get; }

    public int Flavors { get; }

    public int LegacyId => 0;

    public BulbOrigin Origin => BulbOrigin.BuiltIn;

    public string ContentKey => Id;

    public string Name => Id;

    public string Description => "";

    public string Author => "";

    public string Copyright => "";

    public IReadOnlyList<string> Categories => [];

    public string? FilePath => null;

    public bool IsLocked => true;

    public bool IsEditable => false;

    public bool HasDamagedArt => false;

    public int HorizontalSpacing => 0;

    public int VerticalSpacing => 0;

    public RectI PreviewWindow => new(0, 0, size.Width, size.Height);

    public int GetFlavorCount(Side side) => Flavors;

    public int GetPhaseCount(Side side) => frames;

    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor) => new(kind, frames, 0, size);

    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => size;

    public BulbCell GetCell(CellSlot slot, int flavor, int phase)
    {
        int frame = phase % frames;
        uint color = kind == BulbAnimationKind.LightBulb && frame == 0
            ? 0xFFFF4020u
            : 0xFF000000u | (uint)(0x302010 * (frame + 1) + 0x10101 * (flavor % 4));
        var pixels = new uint[size.Width * size.Height];
        for (int y = 4; y < size.Height - 4; y++)
        {
            for (int x = 4; x < size.Width - 4; x++)
            {
                pixels[y * size.Width + x] = color;
            }
        }

        return new BulbCell(new Rgba32Image(size.Width, size.Height, pixels), null, false);
    }
}

/// <summary>Builds small layouts of synthetic bulbs on an imaginary display.</summary>
internal static class SyntheticScene
{
    public static readonly DisplayInfo Display = new()
    {
        DeviceId = "test-display",
        DeviceName = @"\\.\DISPLAY9",
        Number = 1,
        Bounds = new RectI(0, 0, 400, 300),
        WorkArea = new RectI(0, 0, 400, 300),
        Dpi = 96,
        IsPrimary = true,
    };

    public static SyntheticBulb Light { get; } = new("builtin:test-light", BulbAnimationKind.LightBulb, 2, flavors: 3);

    public static SyntheticBulb Animated { get; } = new("builtin:test-animated", BulbAnimationKind.Animation, 4);

    public static SyntheticBulb Still { get; } = new("builtin:test-static", BulbAnimationKind.Static, 1);

    public static FakeBulbResolver Resolver { get; } = new(Light, Animated, Still);

    /// <summary>Light bulbs on the edges, the given bulb in the corners.</summary>
    public static LightsLayout Layout(string? corner = "builtin:test-animated", string side = "builtin:test-light") =>
        FakeLayout.Build([Display], side, corner, flavors: 3);

    public static FlashOptions Options(FlashPatternId pattern, bool smooth = true) => new() { Pattern = pattern, SmoothFading = smooth };
}
