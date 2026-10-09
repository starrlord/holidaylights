using HolidayLights.App.BulbFactory;
using HolidayLights.Core.Bulbs;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>
/// Acceptance scenario 18 (PRODUCT-SPEC 7.5): a GIF becomes a bulb, gets a second flavor and a category, is saved, loads
/// under the 5.4 rules and exports with "Export Bulb File...".
/// </summary>
public sealed class BulbEditingRoundTripTests : IDisposable
{
    private readonly EditorTestBed bed = new();

    public void Dispose() => bed.Dispose();

    [Fact]
    public async Task AGifBecomesABulbThatIsEditedSavedLoadedAndExported()
    {
        var dialogs = new BulbFactoryDialogs(new FakeAppServices(bed));
        GifBulbCreation created = dialogs.CreateBulbFromGif(bed.Scratch("Candle.gif", NumberedGif(7)));
        Assert.True(created.Succeeded);
        Assert.True(bed.Catalog.TryGetBulb(created.BulbId!, out IBulb? fresh));
        Assert.True(fresh.IsEditable);

        BulbEditingViewModel editor = await bed.OpenAsync(created.BulbId!);
        editor.Frames.SelectedSlot = SlotChoice.For(CellSlot.Top);
        editor.Frames.SelectedFlavor = 1;
        await editor.Frames.ImportFilesAsync([bed.Scratch("Flame.gif", NumberedGif(8))]);
        bed.Prompts.NewCategories.Enqueue("Candles");
        editor.Information.NewCategoryCommand.Execute(null);
        await editor.SaveCommand.ExecuteAsync();

        Assert.True(editor.Result.Saved);
        BulFile saved = BulFile.Read(created.FilePath!);
        Assert.Empty(saved.Problems);
        Assert.False(saved.Locked);
        Assert.Equal(2, saved.GetFlavorCount(Side.Top));
        Assert.Equal(["Candles"], saved.Categories);
        Assert.Equal(2, saved.Entries.Count(e => e.Size != 0));
        Assert.All(References(saved), r => Assert.NotEqual(0, saved.Entries[saved.GetEntryIndex(r.Slot, r.Flavor)].Size));
        Assert.True(bed.Catalog.TryGetBulb(created.BulbId!, out IBulb? reloaded));
        Assert.Equal(2, reloaded.GetFlavorCount(Side.Top));

        string exported = Path.Combine(Path.GetDirectoryName(created.FilePath!)!, "..", "Candle for Grandma.bul");
        BulbExport.Copy(created.FilePath!, exported);
        Assert.Equal(File.ReadAllBytes(created.FilePath!), File.ReadAllBytes(exported));
        Assert.Empty(BulFile.Read(exported).Problems);
    }
}
