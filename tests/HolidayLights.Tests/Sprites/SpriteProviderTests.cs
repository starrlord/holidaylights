using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>The sprite provider: sizes, key normalization, one computation per sprite, halos, memory and disk caching.</summary>
public sealed class SpriteProviderTests : IDisposable
{
    private readonly TempDataRoot dataRoot = new();

    public void Dispose() => dataRoot.Dispose();

    [Fact]
    public void GetSprite_HasTheArtScaleSizeAndIsSharedFromMemory()
    {
        SpriteProvider provider = NewProvider();
        TestBuiltInBulb bulb = BuiltInTestBulbs.Instance["standard-bulbs"];

        PremultipliedImage first = provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);
        PremultipliedImage second = provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);

        Assert.Equal(new SizeI(48, 48), first.Size);
        Assert.Same(first, second);
        Assert.Equal(PixelArtScaler.Scale(bulb.GetCell(CellSlot.Top, 0, 0).Image, 1.5, SpriteStyle.Smooth).Pixels, first.Pixels);
    }

    [Fact]
    public void GetSprite_KeysFollowThe54FlavorAndFrameRules()
    {
        SpriteProvider provider = NewProvider();
        var bulb = new FakeBulb("user:five", BulbAnimationKind.Animation, litFrame: 0, flavorCount: 5,
            Art.Solid(4, 4, Art.Red), Art.Solid(4, 4, Art.Green));

        Assert.Same(provider.GetSprite(bulb, CellSlot.Left, 2, 1, 2.0, SpriteStyle.Crisp),
            provider.GetSprite(bulb, CellSlot.Left, 7, 3, 2.0, SpriteStyle.Crisp));
        Assert.Same(provider.GetSprite(bulb, CellSlot.TopLeft, 0, 0, 2.0, SpriteStyle.Crisp),
            provider.GetSprite(bulb, CellSlot.TopLeft, 4, 2, 2.0, SpriteStyle.Crisp));
        Assert.NotSame(provider.GetSprite(bulb, CellSlot.Left, 0, 0, 2.0, SpriteStyle.Crisp),
            provider.GetSprite(bulb, CellSlot.Left, 0, 0, 2.0, SpriteStyle.Smooth));
        Assert.Equal(4, bulb.CellReads);
    }

    [Fact]
    public void GetSprite_ConcurrentRequestsComputeOnce()
    {
        SpriteProvider provider = NewProvider();
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(32);
        var bulb = new FakeBulb("user:busy", BulbAnimationKind.LightBulb, lit, unlit);

        PremultipliedImage[] results = [.. Enumerable.Range(0, 16).AsParallel().WithDegreeOfParallelism(8)
            .Select(_ => provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1.75, SpriteStyle.Smooth))];

        Assert.All(results, r => Assert.Same(results[0], r));
        Assert.Equal(1, bulb.CellReads);
    }

    [Fact]
    public void GetGlow_BakesTheHaloOfLightBulbsOnly()
    {
        SpriteProvider provider = NewProvider();
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(32);
        var light = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        var animation = new FakeBulb("user:anim", BulbAnimationKind.Animation, lit, unlit);
        var still = new FakeBulb("user:still", BulbAnimationKind.Static, lit);

        GlowSprite? glow = provider.GetGlow(light, CellSlot.Bottom, 0, 1.5);

        Assert.NotNull(glow);
        int margin = GlowBaker.MarginFor(GlowBaker.SigmaFor(new SizeI(48, 48), 1.5));
        Assert.Equal((-margin, -margin), (glow.OffsetX, glow.OffsetY));
        Assert.Equal(new SizeI(48 + 2 * margin, 48 + 2 * margin), glow.Image.Size);
        Assert.Same(glow, provider.GetGlow(light, CellSlot.Bottom, 0, 1.5));
        Assert.Null(provider.GetGlow(animation, CellSlot.Bottom, 0, 1.5));
        Assert.Null(provider.GetGlow(still, CellSlot.Bottom, 0, 1.5));
    }

    [Fact]
    public void GetGlow_FollowsTheLitFrameOfTheAnimation()
    {
        SpriteProvider provider = NewProvider();
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(16);
        var litFirst = new FakeBulb("user:a", BulbAnimationKind.LightBulb, litFrame: 0, flavorCount: 1, lit, unlit);
        var litSecond = new FakeBulb("user:b", BulbAnimationKind.LightBulb, litFrame: 1, flavorCount: 1, unlit, lit);

        GlowSprite? first = provider.GetGlow(litFirst, CellSlot.Top, 0, 1.0);
        GlowSprite? second = provider.GetGlow(litSecond, CellSlot.Top, 0, 1.0);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Image.Pixels, second.Image.Pixels);
    }

    [Fact]
    public void GetGlow_IsNullWhenNothingIsEmissive()
    {
        SpriteProvider provider = NewProvider();
        Rgba32Image frame = Art.Disc(16, Art.Red, 5);
        var bulb = new FakeBulb("user:flat", BulbAnimationKind.LightBulb, frame, frame);

        Assert.Null(provider.GetGlow(bulb, CellSlot.Top, 0, 1.0));
        Assert.Null(provider.GetGlow(bulb, CellSlot.Top, 0, 1.0));
        Assert.Equal(2, bulb.CellReads);
    }

    [Fact]
    public async Task PrefetchAsync_PersistsSpritesAndHalosThatANewProviderReadsWithoutDecoding()
    {
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(24);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        SpriteProvider first = NewProvider();
        SpriteRequest[] requests =
        [
            new(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth),
            new(bulb, CellSlot.Top, 0, 1, 1.5, SpriteStyle.Smooth),
            new(bulb, CellSlot.Right, 0, 0, 1.5, SpriteStyle.Crisp),
        ];

        // The presenter with glow on: prefetch, then ask for the halos (the first request bakes and stores each).
        await first.PrefetchAsync(requests);
        GlowSprite? expectedGlow = first.GetGlow(bulb, CellSlot.Top, 0, 1.5);
        Assert.NotNull(first.GetGlow(bulb, CellSlot.Right, 0, 1.5));

        var reopened = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        SpriteProvider second = NewProvider();
        foreach (SpriteRequest request in requests)
        {
            Assert.Equal(
                first.GetSprite(bulb, request.Slot, request.Flavor, request.Frame, request.Scale, request.Style).Pixels,
                second.GetSprite(reopened, request.Slot, request.Flavor, request.Frame, request.Scale, request.Style).Pixels);
        }

        GlowSprite? actualGlow = second.GetGlow(reopened, CellSlot.Top, 0, 1.5);
        Assert.NotNull(expectedGlow);
        Assert.NotNull(actualGlow);
        Assert.Equal((expectedGlow.OffsetX, expectedGlow.OffsetY), (actualGlow.OffsetX, actualGlow.OffsetY));
        Assert.Equal(expectedGlow.Image.Pixels, actualGlow.Image.Pixels);
        Assert.NotNull(second.GetGlow(reopened, CellSlot.Right, 0, 1.5));
        Assert.Equal(0, reopened.CellReads);
    }

    [Fact]
    public async Task PrefetchAsync_BakesAndStoresNoHalos()
    {
        // Regression (product-owner decision 1): with glow off (Classic 2003, Use Less Power, Stop Flashing) nothing asks
        // for a halo, so the prefetch must not bake or store any.
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(24);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        SpriteProvider provider = NewProvider();

        await provider.PrefetchAsync(
        [
            new(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Crisp),
            new(bulb, CellSlot.Top, 0, 1, 1.5, SpriteStyle.Crisp),
            new(bulb, CellSlot.Left, 0, 0, 1.5, SpriteStyle.Crisp),
            new(bulb, CellSlot.Left, 0, 1, 1.5, SpriteStyle.Crisp),
        ]);
        await provider.Disk.Maintenance;

        Assert.Equal(4, bulb.CellReads); // the four Crisp frames, not the Smooth frames a halo is baked from
        Assert.Equal(4, provider.Memory.Count);
        string[] files = [.. Directory.EnumerateFiles(provider.Disk.Root, "*.sprite", SearchOption.AllDirectories).Select(f => Path.GetFileName(f))];
        Assert.Equal(4, files.Length);
        Assert.DoesNotContain(files, f => f.StartsWith("glow-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetGlow_StoresOnlyTheHalosOfPrefetchedSprites()
    {
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(24);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        SpriteProvider provider = NewProvider();
        await provider.PrefetchAsync([new(bulb, CellSlot.Top, 3, 0, 1.5, SpriteStyle.Smooth)]);

        Assert.NotNull(provider.GetGlow(bulb, CellSlot.Top, 3, 1.5)); // the desktop's halo (flavor 3 is flavor 0 here)
        Assert.NotNull(provider.GetGlow(bulb, CellSlot.Top, 0, 0.75)); // a preview's halo
        Assert.NotNull(provider.GetGlow(bulb, CellSlot.Bottom, 0, 1.5)); // a slot the desktop does not show
        await provider.Disk.Maintenance;

        Assert.True(File.Exists(provider.Disk.GetPath(SpriteCacheKey.ForGlow(bulb.ContentKey, CellSlot.Top, 0, 1.5))));
        Assert.Single(Directory.EnumerateFiles(provider.Disk.Root, "glow-*.sprite", SearchOption.AllDirectories));
    }

    [Fact]
    public void GetGlow_TheHaloOfALongStripStaysCloseToItsBulbs()
    {
        // A BirthdayBulb-like strip of 255 x 24 art pixels at 150 %: sigma comes from the shorter side (7.92, margin 24),
        // so the halo is 431 x 84 pixels instead of 889 x 542.
        var lit = Art.Solid(255, 24, Bgra32.Pack(255, 220, 120, 255));
        var unlit = Art.Solid(255, 24, Bgra32.Pack(80, 60, 30, 255));
        var strip = new FakeBulb("user:strip", BulbAnimationKind.LightBulb, lit, unlit);
        SpriteProvider provider = NewProvider();

        GlowSprite? glow = provider.GetGlow(strip, CellSlot.Top, 0, 1.5);

        Assert.NotNull(glow);
        Assert.Equal(new SizeI(431, 84), glow.Image.Size);
        Assert.Equal((-24, -24), (glow.OffsetX, glow.OffsetY));
    }

    [Fact]
    public void GetSprite_AloneNeverWritesTheDiskCache()
    {
        SpriteProvider provider = NewProvider();
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(16);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);

        provider.GetSprite(bulb, CellSlot.Top, 0, 0, 0.37, SpriteStyle.Smooth);
        provider.GetGlow(bulb, CellSlot.Top, 0, 0.37);

        Assert.False(Directory.Exists(provider.Disk.Root) && Directory.EnumerateFiles(provider.Disk.Root, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Evict_DropsTheMemoryAndDiskEntriesOfAContentKey()
    {
        SpriteProvider provider = NewProvider();
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(8, 8, Art.Green));
        var other = new FakeBulb("user:other", BulbAnimationKind.Static, Art.Solid(8, 8, Art.Red));
        await provider.PrefetchAsync([new(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth), new(other, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth)]);
        string folder = Path.Combine(provider.Disk.Root, SpriteDiskStore.ContentFolderName(bulb.ContentKey));
        Assert.True(Directory.Exists(folder));

        provider.Evict(bulb.ContentKey);
        await provider.Disk.Maintenance;

        Assert.False(Directory.Exists(folder));
        Assert.True(Directory.Exists(Path.Combine(provider.Disk.Root, SpriteDiskStore.ContentFolderName(other.ContentKey))));
        provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);
        provider.GetSprite(other, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);
        Assert.Equal(2, bulb.CellReads);
        Assert.Equal(1, other.CellReads);
    }

    [Fact]
    public async Task DamagedCacheFilesAreIgnored()
    {
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Disc(8, Art.Green, 3));
        var request = new SpriteRequest(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);
        SpriteProvider first = NewProvider();
        await first.PrefetchAsync([request]);
        string file = first.Disk.GetPath(SpriteCacheKey.ForFrame(new SpriteKey(bulb.ContentKey, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth)));
        Assert.True(File.Exists(file));
        byte[] data = await File.ReadAllBytesAsync(file);
        await File.WriteAllBytesAsync(file, data[..^7]);

        SpriteProvider second = NewProvider();
        PremultipliedImage sprite = second.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);

        Assert.Equal(first.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth).Pixels, sprite.Pixels);
        Assert.Equal(2, bulb.CellReads);
    }

    [Fact]
    public async Task PrefetchAsync_StopsWhenCancelled()
    {
        SpriteProvider provider = NewProvider();
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(8, 8, Art.Green));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.PrefetchAsync([new SpriteRequest(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth)], cancellation.Token));
        Assert.Equal(0, bulb.CellReads);
    }

    [Fact]
    public void TheMemoryBudgetDropsTheLeastRecentlyUsedSprites()
    {
        // Each 16 x 16 sprite accounts for 1,024 bytes of pixels plus bookkeeping.
        SpriteProvider provider = NewProvider(new SpriteProviderOptions { MemoryBudgetBytes = 3 * 1200 });
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(8, 8, Art.Green));

        provider.GetSprite(bulb, CellSlot.Top, 0, 0, 2.0, SpriteStyle.Smooth);
        provider.GetSprite(bulb, CellSlot.Right, 0, 0, 2.0, SpriteStyle.Smooth);
        provider.GetSprite(bulb, CellSlot.Top, 0, 0, 2.0, SpriteStyle.Smooth);
        provider.GetSprite(bulb, CellSlot.Bottom, 0, 0, 2.0, SpriteStyle.Smooth);
        provider.GetSprite(bulb, CellSlot.Left, 0, 0, 2.0, SpriteStyle.Smooth);
        Assert.Equal(4, bulb.CellReads);
        Assert.Equal(3, provider.Memory.Count);

        provider.GetSprite(bulb, CellSlot.Top, 0, 0, 2.0, SpriteStyle.Smooth);
        provider.GetSprite(bulb, CellSlot.Right, 0, 0, 2.0, SpriteStyle.Smooth);
        Assert.Equal(5, bulb.CellReads);
    }

    [Fact]
    public void Requests_AreValidated()
    {
        SpriteProvider provider = NewProvider();
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(8, 8, Art.Green));

        Assert.Throws<ArgumentNullException>(() => provider.GetSprite(null!, CellSlot.Top, 0, 0, 1, SpriteStyle.Smooth));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetSprite(bulb, (CellSlot)9, 0, 0, 1, SpriteStyle.Smooth));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetSprite(bulb, CellSlot.Top, -1, 0, 1, SpriteStyle.Smooth));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetSprite(bulb, CellSlot.Top, 0, -1, 1, SpriteStyle.Smooth));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetSprite(bulb, CellSlot.Top, 0, 0, 0, SpriteStyle.Smooth));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1, (SpriteStyle)3));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetGlow(bulb, CellSlot.Top, 0, double.NaN));
        Assert.Throws<ArgumentNullException>(() => provider.Evict(null!));
    }

    private SpriteProvider NewProvider(SpriteProviderOptions? options = null) =>
        new(dataRoot.Paths, NullAppLog.Instance, options ?? SpriteProviderOptions.Default);
}
