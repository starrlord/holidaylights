namespace HolidayLights.App.Shell;

/// <summary>A settings change ready to apply: the transform and the change it is recorded as (with its Undo text).</summary>
/// <param name="Transform">Returns the new settings (the same instance when nothing changes).</param>
/// <param name="Change">The kind and the Undo text.</param>
public sealed record SettingsEdit(Func<AppSettings, AppSettings> Transform, SettingsChange Change)
{
    /// <summary>Applies the edit to a store (UI thread).</summary>
    /// <param name="store">The settings store.</param>
    public void ApplyTo(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.Update(Transform, Change);
    }
}

/// <summary>
/// The settings changes the tray, the hot keys, the instance pipe and the Welcome card make, with their Undo texts (they
/// are recorded like changes made in the Settings window, PRODUCT-SPEC 2.3). Pure.
/// </summary>
public static class SettingsActions
{
    /// <summary>"Show Lights" (5.12.1).</summary>
    /// <param name="on">The new state.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit SetLightsOn(bool on) => new(
        s => s.Lights.On == on ? s : s with { Lights = s.Lights with { On = on } },
        SettingsChange.Edit(on ? "Turn on the lights" : "Turn off the lights"));

    /// <summary>"Bulb Drawing": On Desktop or On Top ("Behind the Desktop Icons" is kept, 5.1).</summary>
    /// <param name="drawing">The new location.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit SetDrawing(BulbDrawing drawing) => new(
        s => s.Lights.Drawing == drawing ? s : s with { Lights = s.Lights with { Drawing = drawing } },
        SettingsChange.Edit(DrawingText(drawing)));

    /// <summary>
    /// The location hot key's job (6.6.4): On Desktop and On Top swap; pressed while the lights are off, it turns them on,
    /// on top.
    /// </summary>
    /// <param name="current">The current settings.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit ToggleLocation(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        BulbDrawing target = !current.Lights.On || current.Lights.Drawing == BulbDrawing.Desktop ? BulbDrawing.OnTop : BulbDrawing.Desktop;
        return new SettingsEdit(
            s => s with { Lights = s.Lights with { On = true, Drawing = target } },
            SettingsChange.Edit(DrawingText(target)));
    }

    /// <summary>"Play Holiday Music" (6.1.1): turning it on while "Play the Chosen Songs" is "Never" sets the mode to "Always".</summary>
    /// <param name="enabled">The new state.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit SetMusicEnabled(bool enabled) => new(
        s =>
        {
            if (s.Music.Enabled == enabled)
            {
                return s;
            }

            AppSettings switched = s with { Music = s.Music with { Enabled = enabled } };
            return enabled && s.Current.Music.Mode == PlayMode.Never
                ? switched with { Current = s.Current with { Music = s.Current.Music with { Mode = PlayMode.Always } } }
                : switched;
        },
        SettingsChange.Edit(enabled ? "Play holiday music" : "Turn off holiday music"));

    /// <summary>"Change Themes Automatically on Holidays" (today's theme follows when it is turned on, 5.11).</summary>
    /// <param name="enabled">The new state.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit SetAutomaticThemes(bool enabled) => new(
        s => s.Calendar.Enabled == enabled ? s : s with { Calendar = s.Calendar with { Enabled = enabled } },
        SettingsChange.Edit(enabled ? "Turn on Automatic themes" : "Turn off Automatic themes"));

    /// <summary>"Automatically Start Holiday Lights" (the Run value follows the setting).</summary>
    /// <param name="auto">The new state.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit SetStartup(bool auto) => new(
        s => s.Startup.Auto == auto ? s : s with { Startup = s.Startup with { Auto = auto } },
        SettingsChange.Edit(auto ? "Start Holiday Lights when you sign in" : "Don't start Holiday Lights when you sign in"));

    /// <summary>"Use Classic 2003 Look" (Welcome card): crisp pixels, no glow, no fading, 2003 screen saver motion.</summary>
    /// <returns>The edit.</returns>
    public static SettingsEdit UseClassicLook()
    {
        LookSettings classic = LookPresets.ValuesOf(LookPreset.Classic2003)!;
        return new SettingsEdit(s => s.Look == classic ? s : s with { Look = classic }, SettingsChange.Edit("Use the Classic 2003 look"));
    }

    /// <summary>Loads a theme by hand (tray, Welcome card, <c>--theme</c>): Recent Settings records the replaced values and Automatic themes turn off (6.4.2, 5.11).</summary>
    /// <param name="themes">The theme service.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="now">The time recorded with the Recent Settings entry.</param>
    /// <returns>The edit.</returns>
    public static SettingsEdit LoadTheme(IThemeService themes, ThemeDefinition theme, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(theme);
        var options = new ThemeApplyOptions($"Before {theme.Name}", now, TurnOffAutomaticThemes: true);
        return new SettingsEdit(
            s => themes.Apply(s, theme, options),
            new SettingsChange(SettingsChangeKind.ThemeLoaded, $"Load the {theme.Name} theme"));
    }

    private static string DrawingText(BulbDrawing drawing) =>
        drawing == BulbDrawing.OnTop ? "Draw the bulbs on top of all windows" : "Draw the bulbs on the desktop";
}
