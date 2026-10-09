using HolidayLights.App.Tray;

namespace HolidayLights.Tests.App;

/// <summary>The tray menu exactly as PRODUCT-SPEC 2.2: order, labels with their 5.4 access keys, states, hot keys, Themes submenu.</summary>
public sealed class TrayMenuModelTests
{
    private static readonly string[] AllThemes = ["Autumn Harvest", "Christmas 1", "Classic Lights", "Halloween", "Thanksgiving"];

    private static TrayMenuState State(Func<AppSettings, AppSettings>? change = null, string? matching = "Halloween", bool canSkip = false,
        string? lightsKey = null, string? locationKey = "Ctrl+Alt+Shift+B")
    {
        AppSettings settings = change?.Invoke(new AppSettings()) ?? new AppSettings();
        return new TrayMenuState(settings, matching, "Halloween", AllThemes, canSkip, lightsKey, locationKey);
    }

    private static TrayMenuItem Item(IReadOnlyList<TrayMenuItem> items, TrayCommand command) => items.Single(i => i.Command == command);

    [Fact]
    public void TheMenuHasTheSpecifiedOrderAndLabels()
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenuModel.Build(State());
        string[] layout = [.. items.Select(i => i.Kind switch
        {
            TrayMenuItemKind.Header => "[header]",
            TrayMenuItemKind.Separator => "-",
            _ => i.Text,
        })];

        Assert.Equal(
            [
                "[header]", "-",
                "Show _Lights", "-",
                "Bulbs On _Desktop", "Bulbs On _Top of All Windows", "-",
                "Th_emes", "Play Holiday _Music", "_Next Song", "-",
                "Holiday Lights _Settings…", "Holiday Lights _Help", "_About Holiday Lights", "-",
                "E_xit Holiday Lights",
            ],
            layout);
    }

    [Fact]
    public void SettingsIsTheBoldDefaultAndNothingElse()
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenuModel.Build(State());
        Assert.Equal(TrayCommand.Settings, Assert.Single(items, i => i.IsDefault).Command);
    }

    [Fact]
    public void ChecksAndRadiosFollowTheSettings()
    {
        IReadOnlyList<TrayMenuItem> on = TrayMenuModel.Build(State());
        Assert.True(Item(on, TrayCommand.ShowLights).IsChecked);
        Assert.True(Item(on, TrayCommand.OnDesktop).IsChecked);
        Assert.False(Item(on, TrayCommand.OnTop).IsChecked);
        Assert.False(Item(on, TrayCommand.PlayMusic).IsChecked);

        IReadOnlyList<TrayMenuItem> changed = TrayMenuModel.Build(State(s => s with
        {
            Lights = s.Lights with { On = false, Drawing = BulbDrawing.OnTop },
            Music = s.Music with { Enabled = true },
        }));
        Assert.False(Item(changed, TrayCommand.ShowLights).IsChecked);
        Assert.False(Item(changed, TrayCommand.OnDesktop).IsChecked);
        Assert.True(Item(changed, TrayCommand.OnTop).IsChecked);
        Assert.True(Item(changed, TrayCommand.PlayMusic).IsChecked);
        Assert.Equal(TrayMenuItemKind.Radio, Item(changed, TrayCommand.OnTop).Kind);
        Assert.Equal(TrayMenuItemKind.Check, Item(changed, TrayCommand.ShowLights).Kind);
    }

    [Fact]
    public void TheLocationHotKeyShowsOnTheRadioItWouldSwitchTo()
    {
        IReadOnlyList<TrayMenuItem> desktop = TrayMenuModel.Build(State());
        Assert.Null(Item(desktop, TrayCommand.OnDesktop).Gesture);
        Assert.Equal("Ctrl+Alt+Shift+B", Item(desktop, TrayCommand.OnTop).Gesture);

        IReadOnlyList<TrayMenuItem> onTop = TrayMenuModel.Build(State(s => s with { Lights = s.Lights with { Drawing = BulbDrawing.OnTop } }));
        Assert.Equal("Ctrl+Alt+Shift+B", Item(onTop, TrayCommand.OnDesktop).Gesture);
        Assert.Null(Item(onTop, TrayCommand.OnTop).Gesture);

        IReadOnlyList<TrayMenuItem> none = TrayMenuModel.Build(State(locationKey: null));
        Assert.All(none, i => Assert.Null(i.Gesture));
    }

    [Fact]
    public void TheLightsHotKeyShowsOnShowLightsWhenItIsOn()
    {
        Assert.Null(Item(TrayMenuModel.Build(State()), TrayCommand.ShowLights).Gesture);
        Assert.Equal("Ctrl+Alt+Shift+L", Item(TrayMenuModel.Build(State(lightsKey: "Ctrl+Alt+Shift+L")), TrayCommand.ShowLights).Gesture);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void NextSongNeedsMusicAndASong(bool musicOn, bool canSkip, bool enabled)
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenuModel.Build(State(s => s with { Music = s.Music with { Enabled = musicOn } }, canSkip: canSkip));
        Assert.Equal(enabled, Item(items, TrayCommand.NextSong).IsEnabled);
    }

    [Fact]
    public void EveryOtherItemIsAlwaysEnabled()
    {
        IReadOnlyList<TrayMenuItem> items = TrayMenuModel.Build(State(s => s with { Lights = s.Lights with { On = false } }));
        Assert.All(items.Where(i => i.Command is not null and not TrayCommand.NextSong), i => Assert.True(i.IsEnabled));
    }

    [Fact]
    public void TheThemesSubmenuNamesTodaysThemeThenEveryThemeThenManageThemes()
    {
        TrayMenuItem themes = TrayMenuModel.Build(State()).Single(i => i.Kind == TrayMenuItemKind.Submenu);
        IReadOnlyList<TrayMenuItem> children = themes.Children!;
        Assert.Equal("_Automatic (Halloween Today)", children[0].Text);
        Assert.Equal(TrayMenuItemKind.Check, children[0].Kind);
        Assert.True(children[0].IsChecked);
        Assert.Equal(TrayMenuItemKind.Separator, children[1].Kind);
        Assert.Equal(AllThemes, children.Where(c => c.Command == TrayCommand.LoadTheme).Select(c => c.ThemeName));
        Assert.Equal(TrayMenuItemKind.Separator, children[^2].Kind);
        Assert.Equal("_Manage Themes…", children[^1].Text);
        Assert.Equal("Halloween", Assert.Single(children, c => c.Command == TrayCommand.LoadTheme && c.IsChecked).ThemeName);
    }

    [Fact]
    public void NoThemeRadioIsOnForCustomSettings()
    {
        TrayMenuItem themes = TrayMenuModel.Build(State(s => s with { Calendar = s.Calendar with { Enabled = false } }, matching: null)).Single(i => i.Kind == TrayMenuItemKind.Submenu);
        Assert.DoesNotContain(themes.Children!, c => c.IsChecked);
    }

    [Fact]
    public void UnderscoresInThemeNamesAreNotAccessKeys()
    {
        var state = new TrayMenuState(new AppSettings(), "My_Theme", "My_Theme", ["My_Theme"], false, null, null);
        TrayMenuItem themes = TrayMenuModel.Build(state).Single(i => i.Kind == TrayMenuItemKind.Submenu);
        Assert.Contains(themes.Children!, c => c.Text == "My__Theme" && c.ThemeName == "My_Theme");
        Assert.Equal("_Automatic (My__Theme Today)", themes.Children![0].Text);
    }

    [Fact]
    public void TheHeaderSaysWhoChoseTheTheme()
    {
        Assert.Equal(new TrayHeader("Halloween", "Automatic theme"), TrayMenuModel.Header(State()));
        Assert.Equal(new TrayHeader("Christmas 1", "Chosen by you"), TrayMenuModel.Header(State(matching: "Christmas 1")));
        Assert.Equal(new TrayHeader("Halloween", "Chosen by you"), TrayMenuModel.Header(State(s => s with { Calendar = s.Calendar with { Enabled = false } })));
        Assert.Equal(new TrayHeader("Custom Settings", ""), TrayMenuModel.Header(State(matching: null)));
    }
}
