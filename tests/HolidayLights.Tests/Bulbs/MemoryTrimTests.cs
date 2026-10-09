using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;
using HolidayLights.Tests.Sprites;

namespace HolidayLights.Tests.Bulbs;

/// <summary>Review r1 #19: the caches the Settings previews fill are given back when Settings closes.</summary>
public sealed class MemoryTrimTests : IDisposable
{
    private readonly TempDataRoot dataRoot = new();

    public void Dispose() => dataRoot.Dispose();

    [Fact]
    public void TheCatalog_DropsItsBulbObjects_AndReadsThemAgainOnDemand()
    {
        using var harness = new CatalogHarness(CatalogHarness.SmallBundledSet);
        BulbCatalog catalog = harness.Start();
        Assert.True(catalog.TryGetBulb("addon:Arrow", out IBulb? first));
        Assert.True(catalog.TryGetBulb("addon:Arrow", out IBulb? cached));
        Assert.Same(first, cached);

        catalog.TrimMemory();

        Assert.True(catalog.TryGetBulb("addon:Arrow", out IBulb? again));
        Assert.NotSame(first, again);
        Assert.Equal(first.ContentKey, again.ContentKey);
    }

    [Fact]
    public void TheDecodedArtCache_Empties()
    {
        var cache = new DecodedArtCache(1024 * 1024);
        cache.Add("a|0", [new Rgba32Image(8, 8)]);
        Assert.Equal(256, cache.SizeBytes);

        cache.Clear();

        Assert.Equal(0, cache.SizeBytes);
        Rgba32Image[] decoded = [new Rgba32Image(4, 4)];
        Assert.Same(decoded, cache.GetOrAdd("a|0", () => decoded));
    }

    [Fact]
    public void TheSpriteMemoryCache_Empties_AndSpritesAreMadeAgain()
    {
        var provider = new SpriteProvider(DataPaths.ForDataRoot(dataRoot.Root), new RecordingLog());
        TestBuiltInBulb bulb = BuiltInTestBulbs.Instance["standard-bulbs"];
        PremultipliedImage first = provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);
        Assert.Equal(1, provider.Memory.Count);

        provider.TrimMemory();

        Assert.Equal(0, provider.Memory.Count);
        Assert.Equal(0, provider.Memory.Bytes);
        PremultipliedImage again = provider.GetSprite(bulb, CellSlot.Top, 0, 0, 1.5, SpriteStyle.Smooth);
        Assert.Equal(first.Pixels, again.Pixels);
    }
}
