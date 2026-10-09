namespace HolidayLights.App.Tray;

/// <summary>What a tray menu item does (PRODUCT-SPEC 2.2).</summary>
public enum TrayCommand
{
    /// <summary>"Show Lights".</summary>
    ShowLights,

    /// <summary>"Bulbs On Desktop".</summary>
    OnDesktop,

    /// <summary>"Bulbs On Top of All Windows".</summary>
    OnTop,

    /// <summary>"Automatic (&lt;theme&gt; Today)" in the Themes submenu.</summary>
    AutomaticThemes,

    /// <summary>A theme in the Themes submenu (<see cref="TrayMenuItem.ThemeName"/>).</summary>
    LoadTheme,

    /// <summary>"Manage Themes...".</summary>
    ManageThemes,

    /// <summary>"Play Holiday Music".</summary>
    PlayMusic,

    /// <summary>"Next Song".</summary>
    NextSong,

    /// <summary>"Holiday Lights Settings..." (the bold default item).</summary>
    Settings,

    /// <summary>"Holiday Lights Help".</summary>
    Help,

    /// <summary>"About Holiday Lights".</summary>
    About,

    /// <summary>"Exit Holiday Lights".</summary>
    Exit,
}

/// <summary>The kind of a tray menu entry.</summary>
public enum TrayMenuItemKind
{
    /// <summary>The header strip (not focusable).</summary>
    Header,

    /// <summary>A separator.</summary>
    Separator,

    /// <summary>A check item.</summary>
    Check,

    /// <summary>A radio item.</summary>
    Radio,

    /// <summary>A command.</summary>
    Command,

    /// <summary>A submenu (Themes).</summary>
    Submenu,
}

/// <summary>One tray menu entry.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Command">What it does (null for the header, separators and the submenu).</param>
/// <param name="Text">The label with its WPF access key ("Show _Lights"); empty for separators and the header.</param>
/// <param name="IsChecked">Check and radio state.</param>
/// <param name="IsEnabled">False when greyed out ("Next Song" without music).</param>
/// <param name="Gesture">The hot key shown at the right ("Ctrl+Alt+Shift+B"), or null.</param>
/// <param name="IsDefault">The bold default item.</param>
/// <param name="ThemeName">The theme of a <see cref="TrayCommand.LoadTheme"/> item.</param>
/// <param name="Children">The items of a submenu.</param>
public sealed record TrayMenuItem(
    TrayMenuItemKind Kind,
    TrayCommand? Command,
    string Text,
    bool IsChecked = false,
    bool IsEnabled = true,
    string? Gesture = null,
    bool IsDefault = false,
    string? ThemeName = null,
    IReadOnlyList<TrayMenuItem>? Children = null)
{
    /// <summary>A separator.</summary>
    public static TrayMenuItem Separator { get; } = new(TrayMenuItemKind.Separator, null, "");
}

/// <summary>The two lines beside the header strip.</summary>
/// <param name="ThemeLine">The theme name or "Custom Settings".</param>
/// <param name="SourceLine">"Automatic theme", "Chosen by you" or empty.</param>
public sealed record TrayHeader(string ThemeLine, string SourceLine);

/// <summary>Everything the tray menu shows.</summary>
/// <param name="Settings">The settings.</param>
/// <param name="MatchingTheme">The theme whose 13 values equal the current settings, or null ("Custom Settings").</param>
/// <param name="TodayTheme">The theme the calendar resolves for today.</param>
/// <param name="ThemeNames">Every theme, A-Z.</param>
/// <param name="CanSkipSong">A song is playing or waiting, so "Next Song" can act.</param>
/// <param name="LightsHotKey">The registered "Turn the Lights On or Off" combination ("Ctrl+Alt+Shift+L"), or null.</param>
/// <param name="LocationHotKey">The registered location combination ("Ctrl+Alt+Shift+B"), or null.</param>
public sealed record TrayMenuState(
    AppSettings Settings,
    string? MatchingTheme,
    string TodayTheme,
    IReadOnlyList<string> ThemeNames,
    bool CanSkipSong,
    string? LightsHotKey,
    string? LocationHotKey);

/// <summary>Builds the tray menu exactly as PRODUCT-SPEC 2.2 (frequent actions first, every 5.4 label kept). Pure.</summary>
public static class TrayMenuModel
{
    /// <summary>"Custom Settings": the settings equal no theme.</summary>
    public const string CustomSettings = "Custom Settings";

    /// <summary>The header lines: the theme (or "Custom Settings") and who chose it.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The header.</returns>
    public static TrayHeader Header(TrayMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.MatchingTheme is null)
        {
            return new TrayHeader(CustomSettings, "");
        }

        return new TrayHeader(state.MatchingTheme, IsAutomatic(state) ? "Automatic theme" : "Chosen by you");
    }

    /// <summary>True when the current settings are the theme Automatic themes chose for today.</summary>
    /// <param name="state">The state.</param>
    /// <returns>True for an automatic theme.</returns>
    public static bool IsAutomatic(TrayMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Settings.Calendar.Enabled && state.MatchingTheme is not null
            && string.Equals(state.MatchingTheme, state.TodayTheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The menu items in order, the header first.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<TrayMenuItem> Build(TrayMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        AppSettings settings = state.Settings;
        bool onTop = settings.Lights.Drawing == BulbDrawing.OnTop;
        return
        [
            new TrayMenuItem(TrayMenuItemKind.Header, null, ""),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayMenuItemKind.Check, TrayCommand.ShowLights, "Show _Lights", IsChecked: settings.Lights.On, Gesture: state.LightsHotKey),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayMenuItemKind.Radio, TrayCommand.OnDesktop, "Bulbs On _Desktop", IsChecked: !onTop, Gesture: onTop ? state.LocationHotKey : null),
            new TrayMenuItem(TrayMenuItemKind.Radio, TrayCommand.OnTop, "Bulbs On _Top of All Windows", IsChecked: onTop, Gesture: onTop ? null : state.LocationHotKey),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayMenuItemKind.Submenu, null, "Th_emes", Children: BuildThemes(state)),
            new TrayMenuItem(TrayMenuItemKind.Check, TrayCommand.PlayMusic, "Play Holiday _Music", IsChecked: settings.Music.Enabled),
            new TrayMenuItem(TrayMenuItemKind.Command, TrayCommand.NextSong, "_Next Song", IsEnabled: settings.Music.Enabled && state.CanSkipSong),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayMenuItemKind.Command, TrayCommand.Settings, "Holiday Lights _Settings…", IsDefault: true),
            new TrayMenuItem(TrayMenuItemKind.Command, TrayCommand.Help, "Holiday Lights _Help"),
            new TrayMenuItem(TrayMenuItemKind.Command, TrayCommand.About, "_About Holiday Lights"),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayMenuItemKind.Command, TrayCommand.Exit, "E_xit Holiday Lights"),
        ];
    }

    /// <summary>Doubles the underscores of a name so WPF shows them instead of taking them as access keys.</summary>
    /// <param name="text">A theme name.</param>
    /// <returns>The escaped text.</returns>
    public static string EscapeAccessKeys(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("_", "__", StringComparison.Ordinal);
    }

    private static IReadOnlyList<TrayMenuItem> BuildThemes(TrayMenuState state)
    {
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuItemKind.Check, TrayCommand.AutomaticThemes, $"_Automatic ({EscapeAccessKeys(state.TodayTheme)} Today)",
                IsChecked: state.Settings.Calendar.Enabled),
            TrayMenuItem.Separator,
        };
        items.AddRange(state.ThemeNames.Select(name => new TrayMenuItem(
            TrayMenuItemKind.Radio, TrayCommand.LoadTheme, EscapeAccessKeys(name),
            IsChecked: string.Equals(name, state.MatchingTheme, StringComparison.OrdinalIgnoreCase), ThemeName: name)));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(TrayMenuItemKind.Command, TrayCommand.ManageThemes, "_Manage Themes…"));
        return items;
    }
}
