using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Themes;

/// <summary>Loading, capturing and comparing themes (see <see cref="IThemeService"/>). Owner: core-settings.</summary>
/// <remarks>
/// Songs are converted between the theme model (checked songs) and the settings model (unchecked songs) with every song
/// the <see cref="ISongLibrary"/> knows, hidden ones included, so the library must have scanned its folders
/// (<see cref="ISongLibrary.Start"/>) before themes are applied or compared.
/// </remarks>
public sealed class ThemeService : IThemeService
{
    /// <summary>The label of the entry <see cref="RestoreRecent"/> records.</summary>
    public const string BeforeRestoreLabel = "Before Restore";

    private readonly IThemeLibrary library;
    private readonly ISongLibrary songs;
    private readonly IBulbResolver bulbs;

    /// <summary>Creates the service.</summary>
    /// <param name="library">The theme library (for <see cref="FindMatching"/>).</param>
    /// <param name="songs">The known songs (checked songs = all known songs minus the unchecked ones).</param>
    /// <param name="bulbs">Resolves bulb ids (missing bulbs of a theme).</param>
    public ThemeService(IThemeLibrary library, ISongLibrary songs, IBulbResolver bulbs)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(songs);
        ArgumentNullException.ThrowIfNull(bulbs);
        this.library = library;
        this.songs = songs;
        this.bulbs = bulbs;
    }

    /// <inheritdoc />
    /// <remarks>Recent Settings records the replaced values only when they differ from the theme's.</remarks>
    public AppSettings Apply(AppSettings settings, ThemeDefinition theme, ThemeApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);
        KnownSongs known = KnownSongs.From(songs);
        ThemeableSettings values = ThemeValues.Effective(theme, known);
        return settings with
        {
            Current = values,
            RecentSettings = ThemeValues.Equal(settings.Current, values, known)
                ? settings.RecentSettings
                : RecentSettingsHistory.Record(settings.RecentSettings, options.RecentLabel, options.Now, settings.Current),
            Themes = settings.Themes with { LastName = theme.Name },
            Calendar = options.TurnOffAutomaticThemes && settings.Calendar.Enabled ? settings.Calendar with { Enabled = false } : settings.Calendar,
        };
    }

    /// <inheritdoc />
    /// <remarks>The replaced values are recorded as "Before Restore".</remarks>
    public AppSettings RestoreRecent(AppSettings settings, RecentSettingsEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(entry);
        return settings with
        {
            Current = entry.Values,
            RecentSettings = RecentSettingsHistory.Record(settings.RecentSettings, BeforeRestoreLabel, now, settings.Current),
        };
    }

    /// <inheritdoc />
    public ThemeDefinition Capture(string name, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(settings);
        return ThemeValues.Capture(name, settings.Current, KnownSongs.From(songs));
    }

    /// <inheritdoc />
    public bool Matches(ThemeDefinition theme, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(settings);
        KnownSongs known = KnownSongs.From(songs);
        return ThemeValues.Equal(ThemeValues.Effective(theme, known), settings.Current, known);
    }

    /// <inheritdoc />
    public ThemeDefinition? FindMatching(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        KnownSongs known = KnownSongs.From(songs);
        return library.Themes.FirstOrDefault(theme => ThemeValues.Equal(ThemeValues.Effective(theme, known), settings.Current, known));
    }

    /// <inheritdoc />
    /// <remarks>Includes an add-on bulb used as the screen saver animation.</remarks>
    public IReadOnlyList<string> FindMissingBulbs(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        IEnumerable<string> used = (theme.Arrangement ?? SlotAssignment.Classic54Default).EnumerateBulbIds();
        if (theme.Saver?.Animation is { } animation && SaverAnimations.TryGetBulbId(animation, out string? saverBulb))
        {
            used = used.Append(saverBulb);
        }

        return [.. used.Distinct(BulbIds.Comparer).Where(id => !bulbs.TryGetBulb(id, out _))];
    }

    /// <inheritdoc />
    public ThemeNameCheck ValidateName(string name) => ThemeNames.Validate(name);
}
