using HolidayLights.App.BulbFactory;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>Bulb Editing as a whole: Bulb Information, validation, Save with its Undo step, and closing.</summary>
public sealed class BulbEditingSaveTests : IDisposable
{
    private readonly EditorTestBed bed = new();

    public void Dispose() => bed.Dispose();

    [Fact]
    public async Task Save_ReplacesTheFileKeepsItsNameAndRecordsAnUndoStep()
    {
        string id = bed.AddMyBulb(Document(NumberedGif(1), "Star"));
        string path = bed.Catalog.TryGetInfo(id, out BulbInfo? before) ? before.FilePath! : "";
        BulbEditingViewModel editor = await bed.OpenAsync(id);
        editor.Information.Name = "Bright Star ";
        editor.Information.Author = "Pat Smith\npat@example.com";
        editor.Frames.SelectedSlot = SlotChoice.For(CellSlot.Top);
        await editor.Frames.ImportFilesAsync([bed.Scratch("red.gif", SolidGif(12, 12, Red))]);
        bool closed = false;
        editor.CloseRequested += (_, _) => closed = true;

        await editor.SaveCommand.ExecuteAsync();

        Assert.True(closed);
        Assert.Equal(new BulbEditingResult(true, id, false), editor.Result);
        BulFile saved = BulFile.Read(path);
        Assert.False(saved.IsDamaged);
        Assert.False(saved.Locked);
        Assert.Equal("Bright Star", saved.Name);
        Assert.Equal("Pat Smith\r\npat@example.com", saved.Author);
        Assert.Equal(SolidGif(12, 12, Red), saved.Entries[saved.GetEntryIndex(CellSlot.Top, 0)].Gif.ToArray());
        Assert.True(bed.Catalog.TryGetInfo(id, out BulbInfo? after));
        Assert.Equal("Bright Star", after.Name);
        Assert.Single(bed.Holding.Items);
        Assert.Equal(["Edit Bright Star"], bed.Undo.Steps.Select(s => s.Description));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task TheUndoStepOfASave_SwapsThePreviousFileBackAndForth()
    {
        string id = bed.AddMyBulb(Document(NumberedGif(1), "Star"));
        BulbEditingViewModel editor = await bed.OpenAsync(id);
        editor.Information.Name = "Comet";
        await editor.SaveCommand.ExecuteAsync();
        UndoStep step = Assert.Single(bed.Undo.Steps);

        step.Undo();
        Assert.True(bed.Catalog.TryGetInfo(id, out BulbInfo? undone));
        Assert.Equal("Star", undone.Name);

        step.Redo();
        Assert.True(bed.Catalog.TryGetInfo(id, out BulbInfo? redone));
        Assert.Equal("Comet", redone.Name);
        Assert.Equal("Comet", BulFile.Read(redone.FilePath!).Name);
    }

    [Fact]
    public async Task Save_ReportsCharactersABulbFileCannotHold()
    {
        BulbEditingViewModel editor = await bed.OpenAsync(bed.AddMyBulb(Document(NumberedGif(1), "Star")));

        editor.Information.Description = "Snow ☃ and stars";
        Assert.Equal(BulbFactoryStrings.CharactersWillBeReplaced, editor.Information.CharacterWarning);
        await editor.SaveCommand.ExecuteAsync();

        Assert.True(editor.Result.CharactersReplaced);
    }

    [Fact]
    public async Task ANameIsRequired_AndTheCountersFollowTheText()
    {
        BulbEditingViewModel editor = await bed.OpenAsync(bed.AddMyBulb(Document(NumberedGif(1), "Star")));
        Assert.Equal("4/79", editor.Information.NameCounter);
        Assert.Equal("Bulb Editing - Star", editor.Title);

        editor.Information.Name = "  ";

        Assert.Equal("Give your bulb a name.", editor.ValidationMessage);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Equal("Bulb Editing - Star", editor.Title);
        editor.Information.Name = "Moon";
        Assert.Null(editor.ValidationMessage);
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.Equal("Bulb Editing - Moon", editor.Title);
    }

    [Fact]
    public async Task Closing_AsksOnlyWhenSomethingChanged()
    {
        BulbEditingViewModel editor = await bed.OpenAsync(bed.AddMyBulb(Document(NumberedGif(1), "Star")));

        Assert.False(editor.HasUnsavedChanges);
        Assert.True(await editor.ConfirmCloseAsync());
        Assert.Equal(0, bed.Prompts.DiscardQuestions.Count);

        editor.Information.Copyright = "Copyright 2026 Someone";
        bed.Prompts.Discard = false;
        Assert.False(await editor.ConfirmCloseAsync());
        bed.Prompts.Discard = true;
        Assert.True(await editor.ConfirmCloseAsync());
        Assert.Equal((2, "Star"), bed.Prompts.DiscardQuestions);

        editor.Information.Copyright = "Copyright 2026 Pat Smith";
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task Categories_KeepTheirStoredOrderAndNewOnesAreAddedChecked()
    {
        BulbDocument other = Document(NumberedGif(1), "Egg");
        other.Categories.Add("Painted Eggs");
        bed.AddMyBulb(other, "Egg.bul");
        BulbDocument star = Document(NumberedGif(2), "Star");
        star.Categories.Add("Winter");
        star.Categories.Add("Christmas");
        BulbEditingViewModel editor = await bed.OpenAsync(bed.AddMyBulb(star));
        CategoryChecklist categories = editor.Information.Categories;
        string[] names = [.. categories.Items.Select(i => i.Name)];

        Assert.Equal(names.Order(StringComparer.CurrentCultureIgnoreCase), names);
        Assert.Contains("Painted Eggs", names);
        Assert.DoesNotContain(names, n => n.StartsWith('_'));
        Assert.Equal(["Christmas", "Winter"], categories.Items.Where(i => i.IsChecked).Select(i => i.Name));
        Assert.False(editor.HasUnsavedChanges);

        bed.Prompts.NewCategories.Enqueue(" Snow | Ice ");
        editor.Information.NewCategoryCommand.Execute(null);
        string[] withNew = [.. categories.Items.Select(i => i.Name)];
        Assert.Equal(withNew.Order(StringComparer.CurrentCultureIgnoreCase), withNew);
        Assert.Equal(names.Length + 1, withNew.Length);
        Assert.Equal(["Winter", "Christmas", "Snow   Ice"], categories.CheckedNames);
        Assert.True(editor.HasUnsavedChanges);

        CategoryItem winter = categories.Items.Single(i => i.Name == "Winter");
        winter.IsChecked = false;
        bed.Prompts.NewCategories.Enqueue("WINTER");
        editor.Information.NewCategoryCommand.Execute(null);
        Assert.Equal(withNew.Length, categories.Items.Count);
        Assert.True(winter.IsChecked);
    }

    [Fact]
    public async Task TooManyCategories_BlockSaving()
    {
        BulbEditingViewModel editor = await bed.OpenAsync(bed.AddMyBulb(Document(NumberedGif(1), "Star")));
        foreach (int i in Enumerable.Range(0, 30))
        {
            bed.Prompts.NewCategories.Enqueue($"A category named {i:00}");
            editor.Information.NewCategoryCommand.Execute(null);
        }

        Assert.Equal(BulbFactoryStrings.TooManyCategories, editor.ValidationMessage);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_ShowsWhyTheFileCouldNotBeWritten()
    {
        string id = bed.AddMyBulb(Document(NumberedGif(1), "Star"));
        Assert.True(bed.Catalog.TryGetInfo(id, out BulbInfo? info));
        BulbEditingViewModel editor = await bed.OpenAsync(id);
        editor.Information.Name = "Comet";
        Directory.CreateDirectory(info.FilePath! + ".tmp");

        await editor.SaveCommand.ExecuteAsync();

        Assert.False(editor.Result.Saved);
        Assert.StartsWith("Couldn't save \"Star.bul\": ", editor.ErrorMessage);
        Assert.Equal("Star", BulFile.Read(info.FilePath!).Name);
        Assert.Empty(bed.Undo.Steps);
    }
}
