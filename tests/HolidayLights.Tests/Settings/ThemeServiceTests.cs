using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

public sealed class ThemeServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 18, 30, 0, TimeSpan.FromHours(-5));

    private readonly TempDataRoot root = new();
    private readonly ThemeLibrary library;
    private readonly FakeSongLibrary songs = FakeSongLibrary.Bundled();
    private readonly FakeBulbCatalog bulbs = new(["builtin:standard-bulbs", "builtin:jolly-holly", "builtin:snow-family"]);
    private readonly ThemeService service;

    public ThemeServiceTests()
    {
        library = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        library.Load();
        service = new ThemeService(library, songs, bulbs);
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void Apply_CopiesThe13Values_AndUnchecksEverySongTheThemeDoesNotName()
    {
        ThemeDefinition halloween = library.Find("Halloween")!;

        AppSettings s = service.Apply(new AppSettings(), halloween, new ThemeApplyOptions("Before Halloween", Now, TurnOffAutomaticThemes: false));

        Assert.Equal(halloween.Arrangement, s.Current.Arrangement);
        Assert.Equal(PlayMode.Intermittently, s.Current.Music.Mode);
        Assert.Equal(46 - 5, s.Current.Music.DisabledSongs.Count);
        Assert.DoesNotContain("bundled:Halloween - Eerie.mid", s.Current.Music.DisabledSongs);
        Assert.Contains("bundled:Jingle Bells.mid", s.Current.Music.DisabledSongs);
        Assert.Equal("Halloween", s.Current.Saver.Animation);
        Assert.Equal(SaverMovementStyle.Attraction, s.Current.Saver.Style);
        Assert.Equal(RgbColor.Black, s.Current.Saver.Background);
        Assert.Equal(("Creepy", 60), (s.Current.Saver.Font.Family, s.Current.Saver.Font.SizePt));
        Assert.Equal("bundled:Pumpkin.BMP", s.Current.Saver.Picture);
        Assert.Equal("Halloween", s.Themes.LastName);
        Assert.True(s.Calendar.Enabled);
        Assert.True(service.Matches(halloween, s));
    }

    [Fact]
    public void Apply_ValuesTheThemeLacks_TakeTheirDefaults()
    {
        var sparse = new ThemeDefinition { Name = "Sparse", Saver = new ThemeSaver { Animation = "Leaves" } };
        var start = new AppSettings { Current = new ThemeableSettings { Flash = new FlashSettings { Pattern = FlashPatternId.Twinkle, Interval = 2 } } };

        AppSettings s = service.Apply(start, sparse, new ThemeApplyOptions("Before Sparse", Now, false));

        Assert.Equal(SlotAssignment.Classic54Default, s.Current.Arrangement);
        Assert.Equal(new FlashSettings(), s.Current.Flash);
        Assert.Empty(s.Current.Music.DisabledSongs);
        Assert.Equal(PlayMode.Always, s.Current.Music.Mode);
        Assert.Equal(SaverMovementStyle.FallingLeaves, s.Current.Saver.Style);
        Assert.Equal("Happy Holidays!", s.Current.Saver.Message);
        Assert.Equal(SaverPictures.Default, s.Current.Saver.Picture);
    }

    [Fact]
    public void Apply_RecordsTheReplacedValues_NewestFirst_AtMostFive()
    {
        AppSettings s = new();
        string[] themes = ["Halloween", "Thanksgiving", "Christmas 1", "New Year", "Valentine's Day", "Easter Eggs", "July 4th"];
        foreach (string name in themes)
        {
            s = service.Apply(s, library.Find(name)!, new ThemeApplyOptions($"Before {name}", Now, false));
        }

        Assert.Equal(["Before July 4th", "Before Easter Eggs", "Before Valentine's Day", "Before New Year", "Before Christmas 1"], s.RecentSettings.Select(e => e.Label));
        Assert.True(service.Matches(library.Find("Easter Eggs")!, s with { Current = s.RecentSettings[0].Values }));
        Assert.Equal(Now, s.RecentSettings[0].Date);
    }

    [Fact]
    public void Apply_TheValuesAlreadyShown_RecordsNothing()
    {
        AppSettings s = service.Apply(new AppSettings(), library.Find("Christmas 1")!, new ThemeApplyOptions("Before Christmas 1", Now, false));

        AppSettings again = service.Apply(s, library.Find("Christmas 1")!, new ThemeApplyOptions("Before Christmas 1", Now, false));

        Assert.Single(s.RecentSettings);
        Assert.Single(again.RecentSettings);
    }

    [Fact]
    public void Apply_ByHand_TurnsAutomaticThemesOff()
    {
        AppSettings s = service.Apply(new AppSettings(), library.Find("Christmas 1")!, new ThemeApplyOptions("Before Christmas 1", Now, TurnOffAutomaticThemes: true));

        Assert.False(s.Calendar.Enabled);
    }

    [Fact]
    public void RestoreRecent_AppliesTheEntry_AndRecordsWhatItReplaces()
    {
        AppSettings s = service.Apply(new AppSettings(), library.Find("Halloween")!, new ThemeApplyOptions("Before Halloween", Now, false));
        RecentSettingsEntry before = s.RecentSettings[0];

        AppSettings restored = service.RestoreRecent(s, before, Now.AddHours(1));

        Assert.Same(before.Values, restored.Current);
        Assert.Equal(ThemeService.BeforeRestoreLabel, restored.RecentSettings[0].Label);
        Assert.True(service.Matches(library.Find("Halloween")!, restored with { Current = restored.RecentSettings[0].Values }));
    }

    [Fact]
    public void Capture_ThenMatches_AndFindMatching()
    {
        AppSettings s = service.Apply(new AppSettings(), library.Find("Thanksgiving")!, new ThemeApplyOptions("x", Now, false));
        s = s with { Current = s.Current with { Flash = s.Current.Flash with { Interval = 9 } } };

        Assert.Null(service.FindMatching(s));
        ThemeDefinition captured = service.Capture(" Slow Thanksgiving ", s);
        Assert.Equal("Slow Thanksgiving", captured.Name);
        Assert.Empty(captured.Music!.EnabledSongs!);
        library.Save(captured);

        Assert.Equal("Slow Thanksgiving", service.FindMatching(s)!.Name);
        Assert.Equal("Halloween", service.FindMatching(service.Apply(s, library.Find("Halloween")!, new ThemeApplyOptions("x", Now, false)))!.Name);
    }

    [Fact]
    public void Matches_ComparesSongsAsSets_FontFamiliesIgnoringCase_MissingValuesAsDefaults()
    {
        ThemeDefinition christmas = library.Find("Christmas 1")!;
        AppSettings s = service.Apply(new AppSettings(), christmas, new ThemeApplyOptions("x", Now, false));
        AppSettings reordered = s with
        {
            Current = s.Current with
            {
                Music = s.Current.Music with { DisabledSongs = [.. s.Current.Music.DisabledSongs.Reverse(), "user:Gone.mid"] },
                Saver = s.Current.Saver with { Font = s.Current.Saver.Font with { Family = "LUCIDA HANDWRITING" } },
            },
        };

        Assert.True(service.Matches(christmas, reordered));
        Assert.False(service.Matches(christmas, s with { Current = s.Current with { Saver = s.Current.Saver with { Message = "Merry Christmas" } } }));
        Assert.True(service.Matches(new ThemeDefinition { Name = "Empty" }, new AppSettings()));
    }

    [Fact]
    public void FindMissingBulbs_ListsBulbsThatAreNotInstalled_IncludingTheScreenSaverBulb()
    {
        var theme = new ThemeDefinition
        {
            Name = "Mixed",
            Arrangement = SlotAssignment.Empty.WithEdge(Side.Top, ["builtin:standard-bulbs", "addon:Gone", "ADDON:gone"]).WithCorner(Corner.TopLeft, "user:Lost"),
            Saver = new ThemeSaver { Animation = SaverAnimations.ForBulb("addon:Away") },
        };

        Assert.Equal(["addon:Gone", "user:Lost", "addon:Away"], service.FindMissingBulbs(theme));
        Assert.Empty(service.FindMissingBulbs(library.Find("Christmas 1")!));
    }

    [Theory]
    [InlineData("Grandma's Lights", ThemeNameCheck.Valid)]
    [InlineData("   ", ThemeNameCheck.Empty)]
    [InlineData("Mom/Dad", ThemeNameCheck.InvalidCharacters)]
    [InlineData("Tab\there", ThemeNameCheck.InvalidCharacters)]
    [InlineData("  x  ", ThemeNameCheck.Valid)]
    public void ValidateName(string name, ThemeNameCheck expected) => Assert.Equal(expected, service.ValidateName(name));

    [Fact]
    public void ValidateName_LongNames()
    {
        Assert.Equal(ThemeNameCheck.Valid, service.ValidateName(new string('x', 63)));
        Assert.Equal(ThemeNameCheck.TooLong, service.ValidateName(new string('x', 64)));
    }
}
