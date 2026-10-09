using HolidayLights.Core.Bulbs;

namespace HolidayLights.Tests.Bulbs;

public sealed class BulbCatalogIndexTests
{
    [Fact]
    public async Task StartAsync_ListsBuiltInsAtOnceAndEveryBundledBulbWhenDone()
    {
        using var harness = new CatalogHarness();
        Task indexing = harness.Catalog.StartAsync();

        Assert.True(harness.Catalog.All.Count >= 49);
        Assert.Equal(Enumerable.Range(0, 49).Select(i => BuiltInBulbs.Load()[i].Id), harness.Catalog.All.Take(49).Select(b => b.Id));
        await indexing.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(indexing, harness.Catalog.StartAsync());

        Assert.Equal(1550, harness.Catalog.All.Count);
        Assert.Equal(new BulbCatalogStatus(1501, 1501, true), harness.Catalog.Status);
        Assert.Empty(harness.Catalog.DamagedFiles);
        Assert.Equal(Enumerable.Range(0, 1550), harness.Catalog.All.Select(b => b.OriginalOrder));
        harness.WaitFor(BulbCatalogChange.IndexProgress);
    }

    [Fact]
    public void OriginalOrder_IsTheBuiltInTableThenAddOnsByNameLike54()
    {
        using var harness = new CatalogHarness();
        harness.Start();

        BulbInfo[] addOns = [.. harness.Catalog.All.Skip(49)];

        Assert.All(addOns, b => Assert.Equal(BulbOrigin.BundledAddOn, b.Origin));
        // 5.4 lowers only A-Z and compares bytes, so "10th  Birthday" sorts before "Arrow", and digits before letters.
        Assert.True(Position(addOns, "addon:10thBirthday") < Position(addOns, "addon:Arrow"));
        // Two bulbs called "Snow": the file names break the tie.
        Assert.True(Position(addOns, "addon:Snow") + 1 == Position(addOns, "addon:Snow2"));
        for (int i = 1; i < addOns.Length; i++)
        {
            Assert.True(LegacyCompare(addOns[i - 1].Name, addOns[i].Name) <= 0, $"{addOns[i - 1].Name} / {addOns[i].Name}");
        }
    }

    [Fact]
    public void Info_DescribesEachBulbWithoutDecodingAgain()
    {
        using var harness = new CatalogHarness();
        harness.Start();

        Assert.True(harness.Catalog.TryGetInfo("builtin:standard-bulbs", out BulbInfo? standard));
        Assert.Equal(5, standard.TopFlavorCount);
        Assert.Equal(BulbAnimationKind.LightBulb, standard.TopKind);
        Assert.Equal(new SizeI(32, 32), standard.LargestTopCell);
        Assert.Null(standard.FilePath);
        Assert.Null(standard.FileDate);
        Assert.Equal(new[] { "Christmas", "Light Bulbs" }, standard.Categories);

        Assert.True(harness.Catalog.TryGetInfo("addon:HalloweenBulbs", out BulbInfo? halloween));
        Assert.Equal(8, halloween.TopFlavorCount);
        Assert.True(halloween.IsBig);
        Assert.False(halloween.IsEditable);
        Assert.Equal("Halloween Bulbs", halloween.Name);
        Assert.NotNull(halloween.FileDate);
        Assert.Null(halloween.AddedDate);
        Assert.EndsWith("HalloweenBulbs.bul", halloween.FilePath);

        Assert.True(harness.Catalog.TryGetInfo("addon:BeadBulbs", out BulbInfo? beads));
        Assert.Equal(BulbAnimationKind.LightBulb, beads.TopKind);
        Assert.True(harness.Catalog.TryGetInfo("builtin:north-pole-express", out BulbInfo? train));
        Assert.True(train.IsBig is false);
        Assert.True(harness.Catalog.TryGetInfo("builtin:heavy-duty-bulbs", out BulbInfo? heavy));
        Assert.Equal(new SizeI(30, 26), heavy.LargestTopCell);
    }

    [Fact]
    public void SecondStart_ReadsTheIndexCache()
    {
        using var harness = new CatalogHarness();
        harness.Start();
        string[] first = [.. harness.Catalog.All.Select(Describe)];

        harness.Restart();
        harness.Start();

        Assert.True(File.Exists(harness.Paths.BulbIndexFile));
        Assert.Contains(harness.Log.Entries, e => e.Message.Contains("(1501 from the cache)"));
        Assert.Equal(first, harness.Catalog.All.Select(Describe));
    }

    [Fact]
    public void ChangedFile_IsIndexedAgainAndAnUnreadableCacheIsRebuilt()
    {
        using var harness = new CatalogHarness(CatalogHarness.SmallBundledSet);
        string mine = harness.WriteMyBulb("Mine.bul", new TestBul { Gifs = [TestBulbs.RedGif], Name = "Mine" });
        harness.Start();
        harness.Restart();
        new TestBul { Gifs = [TestBulbs.RedGif], Name = "Renamed" }.WriteTo(mine);
        File.SetLastWriteTimeUtc(mine, DateTime.UtcNow.AddMinutes(1));

        harness.Start();

        Assert.True(harness.Catalog.TryGetInfo("user:Mine", out BulbInfo? info));
        Assert.Equal("Renamed", info.Name);
        Assert.Contains(harness.Log.Entries, e => e.Message.Contains($"({CatalogHarness.SmallBundledSet.Length} from the cache)"));

        File.WriteAllText(harness.Paths.BulbIndexFile, "{ not json");
        harness.Restart();
        harness.Start();
        Assert.Contains(harness.Log.Entries, e => e.Level == AppLogLevel.Warning && e.Message.Contains("index cache could not be read"));
        Assert.Equal(CatalogHarness.SmallBundledSet.Length + 50, harness.Catalog.All.Count);
    }

    [Fact]
    public void DamagedFiles_AreListedNotLoadedAndRememberedInTheCache()
    {
        using var harness = new CatalogHarness(CatalogHarness.SmallBundledSet);
        string path = harness.WriteMyBulb("Broken.bul", new TestBul { Gifs = [TestBulbs.RedGif], PreviewEntry = -1 });
        harness.Start();

        DamagedBulbFile damaged = Assert.Single(harness.Catalog.DamagedFiles);
        Assert.Equal(path, damaged.FilePath);
        Assert.Contains("preview", damaged.Reason);
        Assert.False(harness.Catalog.TryGetInfo("user:Broken", out _));
        Assert.False(harness.Catalog.TryGetBulb("user:Broken", out _));
        Assert.Contains(harness.Log.Entries, e => e.Level == AppLogLevel.Warning && e.Message.Contains("Broken.bul"));
        Assert.Equal(new BulbCatalogStatus(CatalogHarness.SmallBundledSet.Length + 1, CatalogHarness.SmallBundledSet.Length + 1, true), harness.Catalog.Status);

        harness.Restart();
        harness.Start();
        Assert.Single(harness.Catalog.DamagedFiles);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TryGetBulb_ResolvesAddOnsBeforeIndexingReachesThem()
    {
        using var harness = new CatalogHarness(CatalogHarness.SmallBundledSet);
        harness.WriteMyBulb("Mine.bul", new TestBul { Gifs = [TestBulbs.RedGif] });

        Assert.True(harness.Catalog.TryGetBulb("addon:Arrow", out IBulb? arrow));
        Assert.True(harness.Catalog.TryGetBulb("user:Mine", out IBulb? mine));
        Assert.True(harness.Catalog.TryGetBulb("builtin:jolly-holly", out IBulb? holly));
        Assert.Equal("Arrow", arrow.Name);
        Assert.Equal(BulbOrigin.UserAddOn, mine.Origin);
        Assert.Equal("Jolly Holly", holly.Name);
        Assert.False(harness.Catalog.TryGetBulb("addon:NoSuchBulb", out _));
        Assert.False(harness.Catalog.TryGetBulb("builtin:no-such-bulb", out _));
        Assert.False(harness.Catalog.TryGetBulb("user:..\\..\\Arrow", out _));
        Assert.False(harness.Catalog.TryGetBulb("not-an-id", out _));

        harness.Start();
        Assert.True(harness.Catalog.TryGetBulb("addon:Arrow", out IBulb? again));
        Assert.Same(arrow, again);
        Assert.Equal(arrow.ContentKey, again.ContentKey);
        Assert.True(harness.Catalog.TryGetBulb("ADDON:arrow", out IBulb? anyCase));
        Assert.Equal("addon:Arrow", anyCase.Id);
        Assert.True(harness.Catalog.TryGetInfo("USER:mine", out BulbInfo? mineInfo));
        Assert.Equal("user:Mine", mineInfo.Id);
    }

    private static string Describe(BulbInfo b) =>
        $"{b.Id}|{b.Origin}|{b.Name}|{b.Description}|{b.Author}|{b.Copyright}|{string.Join(",", b.Categories)}|" +
        $"{string.Join(",", b.OriginalCategories)}|{b.FilePath}|{b.FileDate}|{b.AddedDate}|{b.OriginalOrder}|{b.TopFlavorCount}|" +
        $"{b.TopKind}|{b.LargestTopCell}|{b.IsBig}|{b.HasDamagedArt}|{b.IsEditable}";

    private static int Position(BulbInfo[] bulbs, string id) => Array.FindIndex(bulbs, b => BulbIds.Comparer.Equals(b.Id, id));

    private static int LegacyCompare(string a, string b) =>
        HolidayLights.Core.Bulbs.Catalog.LegacyNameComparer.Instance.Compare(a, b);
}
