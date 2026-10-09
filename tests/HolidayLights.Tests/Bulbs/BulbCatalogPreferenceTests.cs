using HolidayLights.Core.Bulbs;

namespace HolidayLights.Tests.Bulbs;

public sealed class BulbCatalogPreferenceTests : IDisposable
{
    private readonly CatalogHarness harness;

    public BulbCatalogPreferenceTests()
    {
        harness = new CatalogHarness(CatalogHarness.SmallBundledSet);
        harness.Start();
        harness.ClearEvents();
    }

    private BulbCatalog Catalog => harness.Catalog;

    public void Dispose() => harness.Dispose();

    [Fact]
    public void SetFavorite_UpdatesSettingsWithAnUndoText()
    {
        Catalog.SetFavorite("addon:arrow", true);

        Assert.True(Catalog.IsFavorite("addon:Arrow"));
        Assert.Equal(["addon:arrow"], harness.Settings.Current.Bulbs.Favorites);
        Assert.Equal(SettingsChange.Edit("Add Arrow to Favorites"), harness.Settings.History[^1]);
        harness.WaitFor(BulbCatalogChange.Updated, "addon:Arrow");
        Assert.Equal(["addon:Arrow"], Catalog.Query(new BulbQuery { Filter = BulbFilter.Favorites }).Select(b => b.Id));

        Catalog.SetFavorite("addon:Arrow", true);
        Assert.Single(harness.Settings.History);

        Catalog.SetFavorite("ADDON:ARROW", false);
        Assert.False(Catalog.IsFavorite("addon:Arrow"));
        Assert.Empty(harness.Settings.Current.Bulbs.Favorites);
        Assert.Equal("Remove Arrow from Favorites", harness.Settings.History[^1].Description);
    }

    [Fact]
    public void Hide_RemovesABundledBulbFromTheListAndUnhideBringsItBack()
    {
        Catalog.Hide("addon:Gemstones");

        Assert.DoesNotContain(Catalog.All, b => b.Id == "addon:Gemstones");
        Assert.Equal(["addon:Gemstones"], Catalog.Query(new BulbQuery { Filter = BulbFilter.Removed }).Select(b => b.Id));
        Assert.True(Catalog.TryGetBulb("addon:Gemstones", out _));
        Assert.Equal("Remove Gemstones", harness.Settings.History[^1].Description);
        harness.WaitFor(BulbCatalogChange.Removed, "addon:Gemstones");

        Catalog.Unhide("addon:Gemstones");

        Assert.Contains(Catalog.All, b => b.Id == "addon:Gemstones");
        Assert.Empty(harness.Settings.Current.Bulbs.Hidden);
        Assert.Equal("Restore Gemstones", harness.Settings.History[^1].Description);
        harness.WaitFor(BulbCatalogChange.Updated, "addon:Gemstones");
        Assert.Throws<ArgumentException>(() => Catalog.Hide("builtin:sun"));
        Assert.Throws<ArgumentException>(() => Catalog.Hide("user:Mine"));
        Assert.Throws<ArgumentException>(() => Catalog.Unhide("nonsense"));
    }

    [Fact]
    public void SetCategoryOverrides_StoresNormalizedNamesAndOwnCategoriesRemoveTheOverride()
    {
        Catalog.SetCategoryOverrides("builtin:ghosts", ["Halloween", "  Spooky|Stuff ", "halloween", ""]);

        Assert.Equal(new[] { "Halloween", "Spooky Stuff" }, harness.Settings.Current.Bulbs.CategoryOverrides["builtin:ghosts"]);
        Assert.True(Catalog.TryGetInfo("builtin:ghosts", out BulbInfo? info));
        Assert.Equal(new[] { "Halloween", "Spooky Stuff" }, info.Categories);
        Assert.Equal("Change the categories of Ghosts", harness.Settings.History[^1].Description);
        harness.WaitFor(BulbCatalogChange.Updated, "builtin:ghosts");

        Catalog.SetCategoryOverrides("builtin:ghosts", ["HALLOWEEN"]);
        Assert.Empty(harness.Settings.Current.Bulbs.CategoryOverrides);
        Assert.True(Catalog.TryGetInfo("builtin:ghosts", out info));
        Assert.Equal(new[] { "Halloween" }, info.Categories);

        int changes = harness.Settings.History.Count;
        Catalog.SetCategoryOverrides("builtin:ghosts", ["Halloween"]);
        Assert.Equal(changes, harness.Settings.History.Count);
    }

    [Fact]
    public void SettingsChangedElsewhere_AreFollowed()
    {
        // Undo, Cancel or an import change the preferences without the catalog's own members.
        harness.Settings.Update(
            s => s with { Bulbs = s.Bulbs with { Hidden = ["addon:Arrow"], Favorites = ["builtin:sun"] } },
            new SettingsChange(SettingsChangeKind.UndoRedo));

        harness.WaitFor(BulbCatalogChange.Removed, "addon:Arrow");
        Assert.DoesNotContain(Catalog.All, b => b.Id == "addon:Arrow");
        Assert.Equal(["builtin:sun"], Catalog.Query(new BulbQuery { Filter = BulbFilter.Favorites }).Select(b => b.Id));
    }

    [Fact]
    public void HandEditedSettings_DoNotBreakTheCatalog()
    {
        harness.Settings.Update(
            s => s with
            {
                Bulbs = new BulbPreferences
                {
                    Hidden = [null!, "", "addon:Arrow"],
                    Favorites = ["", "builtin:sun", null!],
                    CategoryOverrides = new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["builtin:Ghosts"] = ["First"],
                        ["builtin:ghosts"] = ["Second", null!],
                        ["builtin:sun"] = null!,
                        [""] = ["Nothing"],
                    },
                },
            },
            new SettingsChange(SettingsChangeKind.Import));

        harness.WaitFor(BulbCatalogChange.Removed, "addon:Arrow");
        Assert.Equal(["addon:Arrow"], Catalog.Query(new BulbQuery { Filter = BulbFilter.Removed }).Select(b => b.Id));
        Assert.Equal(["builtin:sun"], Catalog.Query(new BulbQuery { Filter = BulbFilter.Favorites }).Select(b => b.Id));
        Assert.True(Catalog.TryGetInfo("builtin:ghosts", out BulbInfo? ghosts));
        Assert.Equal(["Second"], ghosts.Categories);
        Assert.Contains("Second", Catalog.GetAllCategoryNames());
        Assert.DoesNotContain("Nothing", Catalog.GetAllCategoryNames());
    }

    [Fact]
    public void RecentlyUsed_FollowsBulbsPutOnTheScreen()
    {
        Assert.Empty(Catalog.RecentlyUsed);

        UseArrangement(a => a.WithEdge(Side.Top, ["addon:Arrow", "builtin:standard-bulbs"]));
        UseArrangement(a => a.WithCorner(Corner.TopLeft, "builtin:candy-canes"));
        UseArrangement(a => a.WithEdge(Side.Bottom, ["builtin:snow-family", "addon:Arrow"]));

        Assert.Equal(["builtin:candy-canes", "addon:Arrow"], Catalog.RecentlyUsed);
    }

    private void UseArrangement(Func<SlotAssignment, SlotAssignment> change) =>
        harness.Settings.Update(
            s => s with { Current = s.Current with { Arrangement = change(s.Current.Arrangement) } },
            SettingsChange.Edit("Arrange"));
}
