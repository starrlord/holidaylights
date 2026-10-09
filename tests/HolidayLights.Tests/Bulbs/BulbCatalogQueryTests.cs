using HolidayLights.Core.Bulbs;

namespace HolidayLights.Tests.Bulbs;

public sealed class BulbCatalogQueryTests : IDisposable
{
    private static readonly string Stjarna = "Stj" + (char)0xE4 + "rna";
    private readonly CatalogHarness harness;

    public BulbCatalogQueryTests()
    {
        var settings = new AppSettings
        {
            Bulbs = new BulbPreferences
            {
                Favorites = ["builtin:candy-canes", "addon:Arrow", "user:Missing"],
                Hidden = ["addon:Snow2"],
                CategoryOverrides = new Dictionary<string, IReadOnlyList<string>> { ["builtin:ghosts"] = ["Halloween", "Spooky Stuff"] },
            },
        };
        harness = new CatalogHarness(CatalogHarness.SmallBundledSet, settings);
        string mine = harness.WriteMyBulb("Mine.bul", new TestBul
        {
            Gifs = [TestBulbs.RedGif], Name = "Mine", Description = "My own snow globe", Categories = "christmas|Mine Category",
        });
        string star = harness.WriteMyBulb("Stjarna.bul", new TestBul { Gifs = [TestBulbs.BlueGif], Name = Stjarna, Categories = null });
        File.SetLastWriteTimeUtc(mine, new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(star, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        harness.Start();
    }

    private BulbCatalog Catalog => harness.Catalog;

    public void Dispose() => harness.Dispose();

    [Fact]
    public void Filters_SelectTheShowChoices()
    {
        Assert.Equal(60, Catalog.All.Count);
        Assert.Equal(60, Query(BulbFilter.All).Count);
        Assert.Equal(49, Query(BulbFilter.BuiltIn).Count);
        Assert.Equal(11, Query(BulbFilter.AddOn).Count);
        Assert.Equal(["user:Mine", "user:Stjarna"], Ids(Query(BulbFilter.MyBulbs)).Order());
        Assert.Equal(["builtin:candy-canes", "addon:Arrow"], Ids(Query(BulbFilter.Favorites)));
        Assert.Equal(["addon:Snow2"], Ids(Query(BulbFilter.Removed)));
        Assert.Equal(11, Catalog.Query(new BulbQuery { AddOnsOnly = true }).Count);
        Assert.Empty(Query(BulbFilter.InUse));
        Assert.Equal(
            ["builtin:jolly-holly", "addon:Snowman"],
            Ids(Catalog.Query(new BulbQuery { Filter = BulbFilter.InUse, InUseIds = new HashSet<string> { "ADDON:snowman", "builtin:jolly-holly" } })));
    }

    [Fact]
    public void CategoryFilters_UseEffectiveCategoriesCaseInsensitively()
    {
        string[] holiday = Ids(Catalog.Query(new BulbQuery { Filter = BulbFilter.Holiday, Categories = ["halloween", "Autumn"] }));
        string[] christmas = Ids(Catalog.Query(new BulbQuery { Filter = BulbFilter.Category, Categories = ["CHRISTMAS"] }));
        string[] spooky = Ids(Catalog.Query(new BulbQuery { Filter = BulbFilter.Category, Categories = ["spooky stuff"] }));

        Assert.Equal(8, holiday.Length);
        Assert.Contains("builtin:jack-o-lanterns", holiday);
        Assert.Contains("builtin:ghosts", holiday);
        // 13 built-ins and 4 bundled add-ons are in Christmas (Snow2 is removed), plus "christmas" in Mine.
        Assert.Equal(18, christmas.Length);
        Assert.Contains("user:Mine", christmas);
        Assert.DoesNotContain("addon:Snow2", christmas);
        Assert.Equal(["builtin:ghosts"], spooky);
        Assert.Empty(Catalog.Query(new BulbQuery { Filter = BulbFilter.Category }));
    }

    [Fact]
    public void HiddenBulbs_StillResolveAndDescribe()
    {
        Assert.DoesNotContain(Catalog.All, b => b.Id == "addon:Snow2");
        Assert.True(Catalog.TryGetInfo("addon:Snow2", out BulbInfo? info));
        Assert.Equal("Snow", info.Name);
        Assert.True(Catalog.TryGetBulb("addon:Snow2", out IBulb? bulb));
        Assert.Equal("Snow", bulb.Name);
    }

    [Fact]
    public void Sorts_OrderByNameAndByNewestFile()
    {
        BulbInfo[] byName = [.. Catalog.Query(new BulbQuery { Sort = BulbSortOrder.Name })];
        BulbInfo[] newest = [.. Catalog.Query(new BulbQuery { Sort = BulbSortOrder.Newest })];

        Assert.Equal("10th  Birthday", byName[0].Name);
        for (int i = 1; i < byName.Length; i++)
        {
            int order = string.Compare(byName[i - 1].Name, byName[i].Name, StringComparison.CurrentCultureIgnoreCase);
            Assert.True(order < 0 || (order == 0 && byName[i - 1].OriginalOrder < byName[i].OriginalOrder));
        }

        Assert.Equal(["user:Mine", "user:Stjarna"], Ids(newest.Take(2)));
        Assert.All(newest.Skip(newest.Length - 49), b => Assert.Equal(BulbOrigin.BuiltIn, b.Origin));
        Assert.Equal(Ids(Catalog.All.Where(b => b.Origin == BulbOrigin.BuiltIn)), Ids(newest.Skip(newest.Length - 49)));
    }

    [Fact]
    public void Search_RanksBestMatchesFirst()
    {
        string[] snow = Ids(Search("snow"));

        Assert.Equal(["builtin:snow-family", "addon:Snow", "addon:Snowman", "addon:Snowman3", "user:Mine"], snow);
        Assert.Equal(["addon:MulticolorBubbleLights"], Ids(Search("bubble LIGHTS")));
        Assert.Equal(["addon:HalloweenBulbs", "addon:Snowman"], Ids(Search("barsby")).Order());
        Assert.Equal(["addon:Snowman3"], Ids(Search("snowman3")));
        Assert.Equal(["builtin:jolly-holly", "builtin:snow-family", "builtin:north-pole-express", "builtin:sun"], Ids(Search("winter")));
        Assert.Equal(["user:Stjarna"], Ids(Search("STJARNA")));
        Assert.Equal(["user:Stjarna"], Ids(Search("stj" + (char)0xC4)));
        Assert.Empty(Search("snow zzzz"));
        Assert.Equal(["builtin:snow-family"], Ids(Catalog.Query(new BulbQuery { Filter = BulbFilter.BuiltIn, SearchText = "snow" })));
        Assert.Equal(60, Search("   ").Count);
    }

    [Fact]
    public void CategoryCounts_MergeSpellingsAndSkipRemovedBulbs()
    {
        IReadOnlyList<BulbCategoryCount> counts = Catalog.GetCategoryCounts();

        Assert.Equal(new BulbCategoryCount("Christmas", 18), counts.Single(c => c.Name.Equals("christmas", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(new BulbCategoryCount("Spooky Stuff", 1), counts);
        Assert.Contains(new BulbCategoryCount("Mine Category", 1), counts);
        Assert.DoesNotContain(counts, c => c.Name is "_0" or "_1" or "");
        Assert.Equal(counts.Select(c => c.Name).Order(StringComparer.CurrentCultureIgnoreCase), counts.Select(c => c.Name));
    }

    [Fact]
    public void AllCategoryNames_IncludeEveryKnownCategory()
    {
        IReadOnlyList<string> names = Catalog.GetAllCategoryNames();

        Assert.Contains("Christmas", names);
        Assert.Contains("Spooky Stuff", names);
        Assert.Contains("Light Bulbs", names);
        Assert.Contains("Birthday", names);
        Assert.Single(names, n => n.Equals("christmas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Info_ReportsOverridesAndOwnCategories()
    {
        Assert.True(Catalog.TryGetInfo("builtin:ghosts", out BulbInfo? ghosts));
        Assert.Equal(new[] { "Halloween", "Spooky Stuff" }, ghosts.Categories);
        Assert.Equal(new[] { "Halloween" }, ghosts.OriginalCategories);
        Assert.True(Catalog.TryGetInfo("user:Mine", out BulbInfo? mine));
        // Added = when the file arrived in My Bulbs (its creation time).
        Assert.InRange(mine.AddedDate!.Value.UtcDateTime, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
        Assert.False(mine.IsEditable);
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero), mine.FileDate);
    }

    private IReadOnlyList<BulbInfo> Query(BulbFilter filter) => Catalog.Query(new BulbQuery { Filter = filter });

    private IReadOnlyList<BulbInfo> Search(string text) => Catalog.Query(new BulbQuery { SearchText = text });

    private static string[] Ids(IEnumerable<BulbInfo> bulbs) => [.. bulbs.Select(b => b.Id)];
}
