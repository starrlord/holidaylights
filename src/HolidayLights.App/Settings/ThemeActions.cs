using System.Globalization;

namespace HolidayLights.App.Settings;

/// <summary>Loading themes and switching Automatic themes from the Settings window (PRODUCT-SPEC 3.1, 3.6.2, 5.11).</summary>
public static class ThemeActions
{
    /// <summary>The Recent Settings label of values replaced by a theme loaded by hand: "Before Halloween".</summary>
    /// <param name="themeName">The theme.</param>
    /// <returns>The label.</returns>
    public static string BeforeLabel(string themeName) => "Before " + themeName;

    /// <summary>
    /// Loads a theme: the values are copied, Recent Settings records the replaced values, Automatic themes turn off when
    /// they were on; one undo step; snackbar "&lt;theme&gt; theme loaded." [Undo] (plus " Automatic themes are off.").
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="host">The Settings window, or null.</param>
    /// <param name="theme">The theme.</param>
    /// <returns>True when Automatic themes were turned off.</returns>
    public static bool Load(IAppServices services, ISettingsHost? host, ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(theme);
        bool automaticWasOn = services.Settings.Current.Calendar.Enabled;
        var options = new ThemeApplyOptions(BeforeLabel(theme.Name), DateTimeOffset.Now, TurnOffAutomaticThemes: true);
        services.Settings.Update(
            s => services.ThemeService.Apply(s, theme, options),
            new SettingsChange(SettingsChangeKind.ThemeLoaded, $"Load the {theme.Name} theme"));
        string text = automaticWasOn ? $"{theme.Name} theme loaded. Automatic themes are off." : $"{theme.Name} theme loaded.";
        if (host is not null)
        {
            host.ShowSnackbar(text, "Undo", host.UndoLast);
            host.Announce($"{theme.Name} theme loaded.");
        }

        return automaticWasOn;
    }

    /// <summary>
    /// Turns Automatic themes on: today's theme (or the between-holidays theme) is applied at once and becomes the active
    /// calendar entry, so the calendar does not apply it again; one undo step.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="host">The Settings window, or null.</param>
    public static void TurnOnAutomatic(IAppServices services, ISettingsHost? host)
    {
        ArgumentNullException.ThrowIfNull(services);
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        AppSettings current = services.Settings.Current;
        CalendarSettings calendar = current.Calendar with { Enabled = true };
        CalendarResolution resolution = services.Calendar.Resolve(calendar, today);
        ThemeDefinition? theme = services.Themes.Find(resolution.ThemeName);
        services.Settings.Update(
            s =>
            {
                AppSettings result = theme is null
                    ? s
                    : services.ThemeService.Apply(s, theme, new ThemeApplyOptions($"Before {theme.Name} (automatic)", DateTimeOffset.Now, TurnOffAutomaticThemes: false));
                return result with { Calendar = s.Calendar with { Enabled = true, ActiveEntryId = resolution.EntryId ?? Core.Seasons.CalendarEntryIds.Between } };
            },
            new SettingsChange(SettingsChangeKind.ThemeLoaded, "Turn on automatic themes"));
        string text = theme is null ? "Automatic themes are on." : $"Automatic themes are on. {theme.Name} theme loaded.";
        if (host is not null)
        {
            host.ShowSnackbar(text, "Undo", host.UndoLast);
            host.Announce(text);
        }
    }

    /// <summary>Turns Automatic themes off (the lights stay as they are); one undo step.</summary>
    /// <param name="services">The services.</param>
    public static void TurnOffAutomatic(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Settings.Update(s => s with { Calendar = s.Calendar with { Enabled = false } }, SettingsChange.Edit("Turn off automatic themes"));
    }

    /// <summary>
    /// The settings with every Theme Calendar row and the between-holidays theme that used a renamed theme following it
    /// (the rename itself and, after Cancel restored the calendar, the rename that Cancel keeps).
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="oldName">The theme's previous name.</param>
    /// <param name="newName">The theme's new name.</param>
    /// <returns>The settings, or <paramref name="settings"/> itself when nothing uses the old name.</returns>
    public static AppSettings FollowRename(AppSettings settings, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);
        CalendarSettings calendar = settings.Calendar;
        bool Uses(string? name) => string.Equals(name, oldName, StringComparison.CurrentCultureIgnoreCase);
        if (!Uses(calendar.Between) && !calendar.Entries.Any(entry => Uses(entry.Theme)))
        {
            return settings;
        }

        return settings with
        {
            Calendar = calendar with
            {
                Entries = [.. calendar.Entries.Select(entry => Uses(entry.Theme) ? entry with { Theme = newName } : entry)],
                Between = Uses(calendar.Between) ? newName : calendar.Between,
            },
        };
    }

    /// <summary>The flash options of a theme for its preview (its own pattern; the desktop's look).</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="settings">The settings (Smooth Fading, Limit Flashing).</param>
    /// <returns>The options.</returns>
    public static FlashOptions FlashOf(ThemeDefinition theme, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(settings);
        return new FlashOptions
        {
            Pattern = theme.Flash?.Pattern ?? FlashPatternId.FlashTogether,
            SmoothFading = settings.Look.SmoothFading,
            LimitFlashing = settings.Accessibility.LimitFlashing,
        };
    }

    /// <summary>The summary line of a theme card: "Twinkle - 12 songs, Always - Santa" ("no music" when no song is checked).</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="songs">The song library (a theme without a song list checks every song).</param>
    /// <returns>The summary.</returns>
    public static string Summary(ThemeDefinition theme, ISongLibrary songs)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(songs);
        string pattern = FlashPatterns.DisplayName(theme.Flash?.Pattern ?? FlashPatternId.FlashTogether);
        int count = theme.Music?.EnabledSongs is { } enabled ? enabled.Count : songs.Songs.Count;
        PlayMode mode = theme.Music?.Mode ?? PlayMode.Always;
        string music = count == 0 || mode == PlayMode.Never
            ? "no music"
            : string.Create(CultureInfo.CurrentCulture, $"{count} {(count == 1 ? "song" : "songs")}, {PlayModeShortName(mode)}");
        return $"{pattern} - {music} - {AnimationName(theme.Saver?.Animation ?? SaverAnimations.Snow, null)}";
    }

    /// <summary>The short name of a play mode for summaries ("Always", "Intermittently", "Saver On").</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The name.</returns>
    public static string PlayModeShortName(PlayMode mode) => mode switch
    {
        PlayMode.Never => "Never",
        PlayMode.SaverOn => "Saver On",
        PlayMode.SaverOff => "Saver Off",
        PlayMode.Intermittently => "Intermittently",
        _ => "Always",
    };

    /// <summary>The name of a screen saver animation: "Snow", "(None)", or "Bulb: Candy Canes".</summary>
    /// <param name="animation">The animation value.</param>
    /// <param name="bulbs">The catalog for bulb names, or null.</param>
    /// <returns>The name.</returns>
    public static string AnimationName(string animation, IBulbCatalog? bulbs)
    {
        ArgumentNullException.ThrowIfNull(animation);
        if (!SaverAnimations.TryGetBulbId(animation, out string? bulbId))
        {
            return animation;
        }

        return bulbs is not null && bulbs.TryGetInfo(bulbId, out BulbInfo? info)
            ? "Bulb: " + info.Name
            : "Bulb: " + (BulbIds.IsValid(bulbId) ? BulbIds.GetKey(bulbId) : bulbId);
    }

    /// <summary>The themes loaded most recently, newest first, from the Recent Settings labels ("Before Halloween").</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>Theme names.</returns>
    public static IReadOnlyList<string> RecentlyLoaded(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        const string prefix = "Before ";
        const string automatic = " (automatic)";
        var names = new List<string>();
        foreach (RecentSettingsEntry entry in settings.RecentSettings.OrderByDescending(e => e.Date))
        {
            if (!entry.Label.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = entry.Label[prefix.Length..];
            if (name.EndsWith(automatic, StringComparison.Ordinal))
            {
                name = name[..^automatic.Length];
            }

            if (name.Length > 0 && !names.Contains(name, StringComparer.CurrentCultureIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
