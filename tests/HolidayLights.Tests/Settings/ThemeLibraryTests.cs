using HolidayLights.Core.Themes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

public sealed class ThemeLibraryTests : IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly RecordingLog log = new();
    private readonly TestHoldingFolder holding;
    private readonly ThemeLibrary library;
    private int changes;

    public ThemeLibraryTests()
    {
        holding = new TestHoldingFolder(root.Paths);
        library = new ThemeLibrary(root.Paths, holding, log);
        library.Changed += (_, _) => changes++;
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void Load_FirstRun_SeedsTheNineteenShippedThemes_SortedAToZ()
    {
        library.Load();

        Assert.Equal(19, Directory.EnumerateFiles(root.Paths.ThemesFolder, "*.json").Count());
        Assert.Equal(library.Themes.Select(t => t.Name).Order(StringComparer.CurrentCultureIgnoreCase), library.Themes.Select(t => t.Name));
        Assert.Equal(19, library.ShippedOriginals.Count);
        Assert.Empty(library.Problems);
        Assert.Empty(library.GetMissingOrChangedShipped());
        Assert.All(library.Themes, t => Assert.False(library.IsChangedFromOriginal(t)));
        Assert.Equal(1, changes);
        Assert.Empty(Directory.EnumerateDirectories(root.Paths.RoamingRoot, "Themes.seed-*"));
    }

    [Fact]
    public void Load_AnExistingEmptyFolder_IsNotSeededAgain()
    {
        Directory.CreateDirectory(root.Paths.ThemesFolder);

        library.Load();

        Assert.Empty(library.Themes);
        Assert.Equal(19, library.GetMissingOrChangedShipped().Count);
    }

    [Fact]
    public void Save_ANewTheme_IsFoundCaseInsensitivelyAndSurvivesALoad()
    {
        library.Load();
        ThemeDefinition theme = Theme("  Grandma's Lights ", FlashPatternId.BulbChase);

        library.Save(theme);

        Assert.Equal("Grandma's Lights", library.Find("grandma's lights")!.Name);
        Assert.Null(library.Find("Grandma's Lights")!.Shipped);
        Assert.True(File.Exists(Path.Combine(root.Paths.ThemesFolder, "Grandma's Lights.json")));
        var reloaded = new ThemeLibrary(root.Paths, holding, log);
        reloaded.Load();
        Assert.Equal(FlashPatternId.BulbChase, reloaded.Find("Grandma's Lights")!.Flash!.Pattern);
        Assert.Equal(20, reloaded.Themes.Count);
    }

    [Fact]
    public void Save_ASameNamedTheme_ReplacesIt_KeepingItsSpellingAndFile()
    {
        library.Load();

        library.Save(Theme("halloween", FlashPatternId.Twinkle));

        ThemeDefinition replaced = library.Find("Halloween")!;
        Assert.Equal("Halloween", replaced.Name);
        Assert.Equal(ShippedThemeKind.Classic, replaced.Shipped);
        Assert.Equal(FlashPatternId.Twinkle, replaced.Flash!.Pattern);
        Assert.Equal(19, library.Themes.Count);
        Assert.Equal(19, Directory.EnumerateFiles(root.Paths.ThemesFolder).Count());
        Assert.True(library.IsChangedFromOriginal(replaced));
        Assert.Equal(["Halloween"], library.GetMissingOrChangedShipped().Select(t => t.Name));
    }

    [Fact]
    public void RestoreShipped_BringsBackMissingAndChangedThemes()
    {
        library.Load();
        library.Save(Theme("Halloween", FlashPatternId.Twinkle));
        library.Delete("New Year");

        library.RestoreShipped(library.GetMissingOrChangedShipped().Select(t => t.Name));

        Assert.Empty(library.GetMissingOrChangedShipped());
        Assert.Equal(FlashPatternId.FlashTogether, library.Find("Halloween")!.Flash!.Pattern);
        Assert.Throws<ArgumentException>(() => library.RestoreShipped(["Grandma's Lights"]));
    }

    [Fact]
    public void Delete_MovesTheFileToTheHoldingFolder_RestoreBringsItBack()
    {
        library.Load();
        string file = Path.Combine(root.Paths.ThemesFolder, "Christmas 2.json");

        HeldItem held = library.Delete("christmas 2");

        Assert.Null(library.Find("Christmas 2"));
        Assert.False(File.Exists(file));
        Assert.Single(holding.Items);

        library.Restore(held);

        Assert.NotNull(library.Find("Christmas 2"));
        Assert.True(File.Exists(file));
        Assert.Throws<ArgumentException>(() => library.Delete("Nothing Like This"));
    }

    [Fact]
    public void Restore_WhenTheNameWasTakenMeanwhile_RenamesTheRestoredTheme()
    {
        library.Load();
        HeldItem held = library.Delete("Halloween");
        library.Save(Theme("Halloween", FlashPatternId.Twinkle));

        library.Restore(held);

        Assert.Equal(FlashPatternId.Twinkle, library.Find("Halloween")!.Flash!.Pattern);
        ThemeDefinition restored = library.Find("Halloween (2)")!;
        Assert.Equal(FlashPatternId.FlashTogether, restored.Flash!.Pattern);
        Assert.Null(restored.Shipped);
    }

    [Fact]
    public void Rename_ChangesTheNameAndTheFile()
    {
        library.Load();

        library.Rename("Christmas 2", "Our Tree");

        Assert.Null(library.Find("Christmas 2"));
        ThemeDefinition renamed = library.Find("Our Tree")!;
        Assert.Null(renamed.Shipped);
        Assert.True(File.Exists(Path.Combine(root.Paths.ThemesFolder, "Our Tree.json")));
        Assert.False(File.Exists(Path.Combine(root.Paths.ThemesFolder, "Christmas 2.json")));
        Assert.Contains(library.GetMissingOrChangedShipped(), t => t.Name == "Christmas 2");
    }

    [Fact]
    public void Rename_OnlyTheCase_KeepsTheFile()
    {
        library.Load();

        library.Rename("Halloween", "HALLOWEEN");

        Assert.Equal("HALLOWEEN", library.Find("halloween")!.Name);
        Assert.Equal(19, Directory.EnumerateFiles(root.Paths.ThemesFolder).Count());
    }

    [Fact]
    public void Rename_ToATakenOrInvalidName_Throws()
    {
        library.Load();

        Assert.Throws<ArgumentException>(() => library.Rename("Halloween", "christmas 1"));
        Assert.Throws<ArgumentException>(() => library.Rename("Halloween", "Spooky/Scary"));
        Assert.Throws<ArgumentException>(() => library.Rename("Missing", "Spooky"));
        Assert.Throws<ArgumentException>(() => library.Save(Theme("", FlashPatternId.Alternating)));
        Assert.Throws<ArgumentException>(() => library.Save(Theme(new string('x', 64), FlashPatternId.Alternating)));
    }

    [Fact]
    public void ReservedFileNames_AreAvoided()
    {
        library.Load();

        library.Save(Theme("CON", FlashPatternId.Alternating));
        library.Save(Theme("nul.night", FlashPatternId.Alternating));

        Assert.True(File.Exists(Path.Combine(root.Paths.ThemesFolder, "CON_.json")));
        Assert.True(File.Exists(Path.Combine(root.Paths.ThemesFolder, "nul.night_.json")));
        var reloaded = new ThemeLibrary(root.Paths, holding, log);
        reloaded.Load();
        Assert.NotNull(reloaded.Find("CON"));
        Assert.NotNull(reloaded.Find("nul.night"));
    }

    [Fact]
    public void UnreadableFiles_AreListedAsProblems_AndNeverDeleted()
    {
        Directory.CreateDirectory(root.Paths.ThemesFolder);
        string broken = Path.Combine(root.Paths.ThemesFolder, "Broken.json");
        string foreign = Path.Combine(root.Paths.ThemesFolder, "Foreign.json");
        File.WriteAllText(broken, "{ not json");
        File.WriteAllText(foreign, """{ "schema": "someone.else/1", "name": "Foreign" }""");
        File.WriteAllText(Path.Combine(root.Paths.ThemesFolder, "Twin A.json"), """{ "name": "Twin" }""");
        File.WriteAllText(Path.Combine(root.Paths.ThemesFolder, "Twin B.json"), """{ "name": "twin" }""");

        library.Load();

        Assert.Equal(["Twin"], library.Themes.Select(t => t.Name));
        Assert.Equal(3, library.Problems.Count);
        Assert.Contains(library.Problems, p => p.FilePath == broken);
        Assert.Contains(library.Problems, p => p.FilePath == foreign);
        Assert.True(File.Exists(broken));
    }

    [Fact]
    public void AValueOfTheWrongType_TakesItsDefault_AndAMissingNameComesFromTheFile()
    {
        Directory.CreateDirectory(root.Paths.ThemesFolder);
        File.WriteAllText(
            Path.Combine(root.Paths.ThemesFolder, "Odd One.json"),
            """{ "schema": "holidaylights.theme/1", "flash": { "pattern": "spin", "interval": 42 }, "saver": { "message": "Hi" } }""");

        library.Load();

        ThemeDefinition theme = library.Find("Odd One")!;
        Assert.Null(theme.Flash!.Pattern);
        Assert.Null(theme.Flash.Interval);
        Assert.Equal("Hi", theme.Saver!.Message);
        Assert.Empty(library.Problems);
    }

    private static ThemeDefinition Theme(string name, FlashPatternId pattern) => new()
    {
        Name = name,
        Arrangement = SlotAssignment.Empty.WithEdge(Side.Top, ["builtin:candy-canes"]),
        Flash = new ThemeFlash { Pattern = pattern, Interval = 4 },
    };
}
