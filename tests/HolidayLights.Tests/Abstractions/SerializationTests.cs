using System.Text.Json;

namespace HolidayLights.Tests.Abstractions;

public sealed class SerializationTests
{
    [Fact]
    public void AppSettings_RoundTripsEveryDefault()
    {
        var settings = new AppSettings();

        string json = HolidayLightsJson.Serialize(settings);
        AppSettings back = HolidayLightsJson.DeserializeSettings(json);

        Assert.Equal(json, HolidayLightsJson.Serialize(back));
        Assert.Equal(SlotAssignment.Classic54Default, back.Current.Arrangement);
    }

    [Fact]
    public void AppSettings_UsesTheAppendixCNames()
    {
        string json = HolidayLightsJson.Serialize(new AppSettings());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("desktop", root.GetProperty("lights").GetProperty("drawing").GetString());
        Assert.Equal("eachDisplay", root.GetProperty("lights").GetProperty("frameMode").GetString());
        Assert.Equal("standard", root.GetProperty("lights").GetProperty("size").GetString());
        Assert.Equal("smooth", root.GetProperty("look").GetProperty("pixels").GetString());
        Assert.Equal("soft", root.GetProperty("look").GetProperty("glow").GetString());
        Assert.Equal("flashTogether", root.GetProperty("current").GetProperty("flash").GetProperty("pattern").GetString());
        Assert.Equal("always", root.GetProperty("current").GetProperty("music").GetProperty("mode").GetString());
        Assert.Equal("bounceOffSides", root.GetProperty("current").GetProperty("saver").GetProperty("style").GetString());
        Assert.Equal("#FF0000", root.GetProperty("current").GetProperty("saver").GetProperty("color").GetString());
        Assert.Equal("center", root.GetProperty("current").GetProperty("saver").GetProperty("placement").GetString());
        Assert.Equal("builtin:jolly-holly", root.GetProperty("current").GetProperty("arrangement").GetProperty("topLeft").GetString());
        Assert.Equal("all", root.GetProperty("saver").GetProperty("showOn").GetString());
        Assert.Equal(16, root.GetProperty("colors").GetProperty("custom").GetArrayLength());
        Assert.Equal("useLessPower", root.GetProperty("rest").GetProperty("energySaver").GetString());
        Assert.Equal("home", root.GetProperty("ui").GetProperty("settings").GetProperty("lastPage").GetString());
        Assert.Equal("tiles", root.GetProperty("ui").GetProperty("gallery").GetProperty("view").GetString());
        Assert.Equal("original", root.GetProperty("ui").GetProperty("gallery").GetProperty("sort").GetString());

        JsonElement location = root.GetProperty("hotkeys").GetProperty("location");
        Assert.True(location.GetProperty("enabled").GetBoolean());
        Assert.Equal(["Ctrl", "Alt", "Shift"], location.GetProperty("modifiers").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("B", location.GetProperty("key").GetString());
    }

    [Fact]
    public void AppSettings_MissingValuesKeepTheirDefaults()
    {
        AppSettings settings = HolidayLightsJson.DeserializeSettings(
            """
            {
              // comments and trailing commas are tolerated
              "lights": { "on": false, "drawing": "onTop" },
              "current": { "flash": { "pattern": "twinkle" } },
              "music": { "enabled": true, },
              "unknownFutureValue": 42
            }
            """);

        Assert.False(settings.Lights.On);
        Assert.Equal(BulbDrawing.OnTop, settings.Lights.Drawing);
        Assert.True(settings.Lights.BehindIcons);
        Assert.Equal(FlashPatternId.Twinkle, settings.Current.Flash.Pattern);
        Assert.Equal(FlashSettings.DefaultInterval, settings.Current.Flash.Interval);
        Assert.True(settings.Music.Enabled);
        Assert.Equal(60, settings.Music.Volume);
        Assert.Equal(SlotAssignment.Classic54Default, settings.Current.Arrangement);
        Assert.Equal(13, settings.Calendar.Entries.Count);
    }

    [Fact]
    public void AppSettings_RoundTripsNonDefaultValues()
    {
        var settings = new AppSettings
        {
            HotKeys = new HotKeySettings { Lights = new HotKeyBinding { Enabled = true, Modifiers = HotKeyModifiers.Win | HotKeyModifiers.Shift, Key = "F12" } },
            Bulbs = new BulbPreferences
            {
                Favorites = ["addon:Arrow"],
                CategoryOverrides = new Dictionary<string, IReadOnlyList<string>> { ["builtin:jack-o-lanterns"] = ["Halloween", "Autumn"] },
            },
            RecentSettings = [new RecentSettingsEntry { Label = "Before Halloween (automatic)", Date = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-7)) }],
            Import54 = new Import54Record { Date = new DateOnly(2026, 10, 8), FactoryDefaults = true, Themes = 11, Report = [new ImportReportItem { Item = "Serial Number", Status = ImportItemStatus.NotImported, Reason = "obsolete" }] },
            Onboarding = new OnboardingState { LastNotified = new Dictionary<string, DateOnly> { ["ThemeChanged"] = new(2026, 10, 1) } },
            Saver = new SaverDeviceSettings { Previous = new ScreenSaverPrevious { ScrnsaveExe = @"C:\WINDOWS\system32\HOLIDA~1.SCR", Active = true, TimeoutSeconds = 1200 } },
            Lights = new LightsSettings { PatternBeforeDance = FlashPatternId.BulbChase, FrameMode = FrameMode.AllDisplaysTogether, Size = BulbSize.ExtraLarge },
        };

        AppSettings back = HolidayLightsJson.DeserializeSettings(HolidayLightsJson.Serialize(settings));

        Assert.Equal(HotKeyModifiers.Win | HotKeyModifiers.Shift, back.HotKeys.Lights.Modifiers);
        Assert.Equal("F12", back.HotKeys.Lights.Key);
        Assert.Equal(["Halloween", "Autumn"], back.Bulbs.CategoryOverrides["builtin:jack-o-lanterns"]);
        Assert.Equal(settings.RecentSettings[0].Date, back.RecentSettings[0].Date);
        Assert.Equal(ImportItemStatus.NotImported, back.Import54!.Report[0].Status);
        Assert.Equal(new DateOnly(2026, 10, 1), back.Onboarding.LastNotified["ThemeChanged"]);
        Assert.Equal(1200, back.Saver.Previous!.TimeoutSeconds);
        Assert.Equal(FlashPatternId.BulbChase, back.Lights.PatternBeforeDance);
        Assert.Equal(FrameMode.AllDisplaysTogether, back.Lights.FrameMode);
        Assert.Equal(BulbSize.ExtraLarge, back.Lights.Size);
    }

    [Fact]
    public void ThemeDefinition_ReadsTheAppendixCExample()
    {
        ThemeDefinition theme = HolidayLightsJson.DeserializeTheme(
            """
            {
              "schema": "holidaylights.theme/1",
              "name": "Halloween",
              "shipped": "classic",
              "arrangement": {
                "top": ["builtin:jack-o-lanterns", "builtin:ghosts"],
                "right": ["builtin:jack-o-lanterns", "builtin:the-grim-reaper"],
                "bottom": ["builtin:jack-o-lanterns", "builtin:zombie-tombstones"],
                "left": ["builtin:jack-o-lanterns", "builtin:the-grim-reaper"],
                "topLeft": "builtin:autumn-leaves", "topRight": "builtin:autumn-leaves", "bottomLeft": null, "bottomRight": null
              },
              "flash": { "pattern": "flashTogether", "interval": 5 },
              "music": { "enabledSongs": ["bundled:Halloween - Eerie.mid"], "mode": "intermittently" },
              "saver": { "animation": "Halloween", "style": "attraction", "message": "Happy Halloween!",
                         "font": { "family": "Creepy", "sizePt": 60, "bold": true, "italic": false, "underline": false, "strikeout": false },
                         "color": "#FF0000", "picture": "bundled:Pumpkin.BMP", "placement": "center" }
            }
            """);

        Assert.Equal("Halloween", theme.Name);
        Assert.Equal(ShippedThemeKind.Classic, theme.Shipped);
        Assert.Equal(["builtin:jack-o-lanterns", "builtin:ghosts"], theme.Arrangement!.Top);
        Assert.Null(theme.Arrangement.BottomLeft);
        Assert.Equal(FlashPatternId.FlashTogether, theme.Flash!.Pattern);
        Assert.Equal(PlayMode.Intermittently, theme.Music!.Mode);
        Assert.Equal(SaverMovementStyle.Attraction, theme.Saver!.Style);
        Assert.Equal(60, theme.Saver.Font!.SizePt);
        Assert.Equal(RgbColor.Red, theme.Saver.Color);
        Assert.Null(theme.Saver.Background);

        ThemeDefinition back = HolidayLightsJson.DeserializeTheme(HolidayLightsJson.Serialize(theme));
        Assert.Equal(theme.Arrangement, back.Arrangement);
        Assert.Equal(theme.Saver.Color, back.Saver!.Color);
    }

    [Fact]
    public void InstanceMessage_RoundTripsAsOneLine()
    {
        var command = new InstanceMessage { Command = InstanceCommand.Open([@"C:\Users\Pat\Downloads\Star.bul", @"D:\gifs\a b.gif"]) };
        var music = new InstanceMessage { Music = new MusicEvent(MusicEventKind.NoteOn, 123456789, 9, 36, 100) };

        string commandLine = command.ToLine();
        Assert.DoesNotContain('\n', commandLine);
        Assert.Contains("\"open\"", commandLine);

        Assert.True(InstanceMessage.TryParseLine(commandLine, out InstanceMessage? commandBack));
        Assert.Equal(InstanceCommandKind.Open, commandBack.Command!.Kind);
        Assert.Equal(command.Command!.Arguments, commandBack.Command.Arguments);

        Assert.True(InstanceMessage.TryParseLine(music.ToLine(), out InstanceMessage? musicBack));
        Assert.Equal(music.Music, musicBack.Music);
        Assert.False(InstanceMessage.TryParseLine("not json", out _));
    }

    [Theory]
    [InlineData(SettingsPageId.Home, "home")]
    [InlineData(SettingsPageId.BulbFactory, "bulbs")]
    [InlineData(SettingsPageId.ScreenSaver, "saver")]
    public void InstanceCommand_UsesTheCommandLinePageNames(SettingsPageId page, string name)
    {
        Assert.Equal([name], InstanceCommand.ShowSettings(page).Arguments);
        Assert.True(InstanceCommand.TryParsePageName(name.ToUpperInvariant(), out SettingsPageId parsed));
        Assert.Equal(page, parsed);
    }
}
