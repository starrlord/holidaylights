using HolidayLights.Core.Seasons;
using HolidayLights.Core.Settings;

namespace HolidayLights.Tests.Settings;

public sealed class SettingsSanitizerTests
{
    [Fact]
    public void ValidSettings_ComeBackAsTheSameInstance()
    {
        var settings = new AppSettings();

        Assert.Same(settings, SettingsSanitizer.Sanitize(settings));
    }

    [Fact]
    public void NumbersAreClampedToTheirRanges()
    {
        var settings = new AppSettings
        {
            Music = new MusicSettings { Volume = 150, SyncOffsetMs = -20 },
            Current = new ThemeableSettings
            {
                Flash = new FlashSettings { Interval = 0 },
                Saver = new SaverLook { Font = new SaverFont { Family = "  ", SizePt = 5 }, Message = "\r\n   " + new string('x', 300) },
            },
            Onboarding = new OnboardingState { LocationHotKeyHints = -3 },
        };

        AppSettings s = SettingsSanitizer.Sanitize(settings);

        Assert.Equal(100, s.Music.Volume);
        Assert.Equal(0, s.Music.SyncOffsetMs);
        Assert.Equal(FlashSettings.DefaultInterval, s.Current.Flash.Interval);
        Assert.Equal("Arial", s.Current.Saver.Font.Family);
        Assert.Equal(18, s.Current.Saver.Font.SizePt);
        Assert.Equal(new string('x', 255), s.Current.Saver.Message);
        Assert.Equal(0, s.Onboarding.LocationHotKeyHints);
    }

    [Fact]
    public void MalformedAndDuplicateIdsAreRemoved_EdgesKeepRepeatsAndAtMostSix()
    {
        var settings = new AppSettings
        {
            Current = new ThemeableSettings
            {
                Arrangement = new SlotAssignment
                {
                    Top = ["builtin:a", "", "standard-bulbs", "builtin:a", "builtin:b", "builtin:c", "builtin:d", "builtin:e", "builtin:f"],
                    TopLeft = "nonsense",
                },
                Music = new CurrentMusic { DisabledSongs = ["bundled:A.mid", "BUNDLED:a.mid", "A.mid"] },
            },
            Bulbs = new BulbPreferences { Favorites = ["addon:Arrow", "ADDON:arrow", "Arrow"] },
            Songs = new HiddenItems { Hidden = ["bundled:x.mid", ""] },
        };

        AppSettings s = SettingsSanitizer.Sanitize(settings);

        Assert.Equal(["builtin:a", "builtin:a", "builtin:b", "builtin:c", "builtin:d", "builtin:e"], s.Current.Arrangement.Top);
        Assert.Null(s.Current.Arrangement.TopLeft);
        Assert.Equal(["bundled:A.mid"], s.Current.Music.DisabledSongs);
        Assert.Equal(["addon:Arrow"], s.Bulbs.Favorites);
        Assert.Equal(["bundled:x.mid"], s.Songs.Hidden);
    }

    [Fact]
    public void UnknownEnumNumbersTakeTheirDefaults()
    {
        AppSettings read = HolidayLightsJson.DeserializeSettings(
            """{ "lights": { "drawing": 7, "size": 99 }, "current": { "flash": { "pattern": 42 }, "saver": { "style": 9, "animation": "Leaves", "placement": 8 } }, "rest": { "energySaver": 12 } }""");

        AppSettings s = SettingsSanitizer.Sanitize(read);

        Assert.Equal(BulbDrawing.Desktop, s.Lights.Drawing);
        Assert.Equal(BulbSize.Standard, s.Lights.Size);
        Assert.Equal(FlashPatternId.FlashTogether, s.Current.Flash.Pattern);
        Assert.Equal(SaverMovementStyle.FallingLeaves, s.Current.Saver.Style);
        Assert.Equal(PicturePlacement.Center, s.Current.Saver.Placement);
        Assert.Equal(EnergySaverChoice.UseLessPower, s.Rest.EnergySaver);
    }

    [Theory]
    [InlineData("snow flakes", "Snow Flakes")]
    [InlineData("Dancing Demon", "Dancing Demon")]
    [InlineData("bulb:addon:Arrow", "bulb:addon:Arrow")]
    [InlineData("bulb:Arrow", "Snow")]
    [InlineData("Fireworks", "Snow")]
    public void Animations_AreCanonicalOrSnow(string stored, string expected)
    {
        var settings = new AppSettings { Current = new ThemeableSettings { Saver = new SaverLook { Animation = stored } } };

        Assert.Equal(expected, SettingsSanitizer.Sanitize(settings).Current.Saver.Animation);
    }

    [Fact]
    public void Pictures_MissingIsTheDefault_MalformedIsNone()
    {
        Assert.Equal(SaverPictures.Default, Picture(""));
        Assert.Equal(SaverPictures.None, Picture("Santa Candle.BMP"));
        Assert.Equal(SaverPictures.None, Picture(SaverPictures.None));
        Assert.Equal("user:Family.jpg", Picture("user:Family.jpg"));

        static string Picture(string stored) =>
            SettingsSanitizer.Sanitize(new AppSettings { Current = new ThemeableSettings { Saver = new SaverLook { Picture = stored } } }).Current.Saver.Picture;
    }

    [Theory]
    [InlineData("b", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift, "B", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData("pageup", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "PageUp", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt)]
    [InlineData("Space", HotKeyModifiers.Ctrl, "B", HotKeyModifiers.Ctrl)]
    [InlineData("Q", HotKeyModifiers.None, "Q", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData("Q", HotKeyModifiers.Shift, "Q", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData("F5", HotKeyModifiers.None, "F5", HotKeyModifiers.None)]
    [InlineData("Pause", HotKeyModifiers.Shift, "Pause", HotKeyModifiers.Shift)]
    [InlineData("NumPad3", HotKeyModifiers.Win, "NumPad3", HotKeyModifiers.Win)]
    public void HotKeys_KeepOnlyKeysAHotKeyCanUseAndNeverTakeATypingKey(string key, HotKeyModifiers modifiers, string expectedKey, HotKeyModifiers expectedModifiers)
    {
        var settings = new AppSettings { HotKeys = new HotKeySettings { Location = new HotKeyBinding { Enabled = true, Key = key, Modifiers = modifiers } } };

        HotKeyBinding location = SettingsSanitizer.Sanitize(settings).HotKeys.Location;

        Assert.Equal(expectedKey, location.Key);
        Assert.Equal(expectedModifiers, location.Modifiers);
    }

    [Fact]
    public void Calendar_RestoresMissingRowsInOrder_RepairsRules_KeepsCustomRows()
    {
        IReadOnlyList<CalendarEntry> defaults = new SeasonCalendar().CreateDefaultEntries("US");
        var stored = new List<CalendarEntry>
        {
            defaults.Single(e => e.Id == "halloween") with { Use = false, Theme = "Spooky" },
            new() { Id = "birthday", Use = true, Theme = "Party", Rule = new CalendarRule { Type = CalendarRuleType.Fixed, From = "05-04", To = "05-04" } },
            defaults.Single(e => e.Id == "newYear") with { Id = "NEWYEAR", Rule = new CalendarRule { Type = CalendarRuleType.Fixed, From = "31-12", To = "01-01" } },
            new() { Id = "broken", Use = true, Theme = "X", Rule = new CalendarRule { Type = CalendarRuleType.Fixed, From = "x" } },
        };
        var settings = new AppSettings { Calendar = new CalendarSettings { Region = " us ", Between = "", Entries = stored } };

        CalendarSettings calendar = SettingsSanitizer.Sanitize(settings).Calendar;

        Assert.Equal("US", calendar.Region);
        Assert.Equal(ShippedThemeNames.ClassicLights, calendar.Between);
        Assert.Equal([.. CalendarEntryIds.All, "birthday"], calendar.Entries.Select(e => e.Id));
        CalendarEntry halloween = calendar.Entries.Single(e => e.Id == "halloween");
        Assert.False(halloween.Use);
        Assert.Equal("Spooky", halloween.Theme);
        Assert.Equal("12-31", calendar.Entries.Single(e => e.Id == "newYear").Rule.From);
    }

    [Theory]
    [InlineData("gb", "GB")]
    [InlineData("419", "419")]
    [InlineData("United States", "US")]
    [InlineData("", "US")]
    public void Calendar_Regions(string stored, string expected) =>
        Assert.Equal(expected, SettingsSanitizer.Sanitize(new AppSettings { Calendar = new CalendarSettings { Region = stored } }).Calendar.Region);

    [Fact]
    public void RecentSettings_KeepTheNewestFive()
    {
        RecentSettingsEntry[] entries = [.. Enumerable.Range(0, 7).Select(i => new RecentSettingsEntry { Label = $"Entry {i}" })];

        AppSettings s = SettingsSanitizer.Sanitize(new AppSettings { RecentSettings = entries });

        Assert.Equal(["Entry 0", "Entry 1", "Entry 2", "Entry 3", "Entry 4"], s.RecentSettings.Select(e => e.Label));
    }

    [Fact]
    public void CategoryOverrides_AreKeyedCaseInsensitively_WithCleanNames()
    {
        AppSettings read = HolidayLightsJson.DeserializeSettings(
            """{ "bulbs": { "categoryOverrides": { "builtin:standard-bulbs": [" Christmas ", "christmas", "", "Light Bulbs"], "nonsense": ["X"] } } }""");

        IReadOnlyDictionary<string, IReadOnlyList<string>> overrides = SettingsSanitizer.Sanitize(read).Bulbs.CategoryOverrides;

        Assert.Equal(["Christmas", "Light Bulbs"], overrides["BUILTIN:Standard-Bulbs"]);
        Assert.Single(overrides);
    }

    [Fact]
    public void WindowPlacement_WithImpossibleValues_IsForgotten()
    {
        var settings = new AppSettings { Ui = new UiSettings { Settings = new SettingsWindowSettings { Window = new WindowPlacementSettings { Width = double.NaN } } } };

        Assert.Null(SettingsSanitizer.Sanitize(settings).Ui.Settings.Window);
    }
}
