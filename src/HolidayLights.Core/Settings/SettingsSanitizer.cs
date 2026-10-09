using HolidayLights.Core.Seasons;

namespace HolidayLights.Core.Settings;

/// <summary>
/// Validation and clamping of <see cref="AppSettings"/> (PRODUCT-SPEC Appendix C, 7.1): null values from a file become
/// their defaults, unknown enum values their defaults, numbers are clamped to their ranges, id lists lose malformed and
/// duplicate ids, and the file version is stamped. Applied after loading and after every update; valid settings come
/// back as the same instance.
/// </summary>
internal static class SettingsSanitizer
{
    /// <summary>The most Recent Settings entries kept (PRODUCT-SPEC 3.6.6).</summary>
    public const int MaxRecentSettings = 5;

    /// <summary>The largest music sync offset in milliseconds.</summary>
    public const int MaxSyncOffsetMs = 1000;

    /// <summary>Repairs every value.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The same instance when valid, else a repaired copy.</returns>
    public static AppSettings Sanitize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppSettings repaired = settings with
        {
            Version = AppSettings.CurrentVersion,
            Lights = Sanitize(settings.Lights),
            Look = Sanitize(settings.Look),
            Current = ThemeableSanitizer.Sanitize(settings.Current),
            Music = Sanitize(settings.Music),
            Saver = Sanitize(settings.Saver),
            Colors = Sanitize(settings.Colors),
            Calendar = CalendarSanitizer.Sanitize(settings.Calendar),
            HotKeys = HotKeySanitizer.Sanitize(settings.HotKeys),
            Startup = settings.Startup ?? new StartupSettings(),
            Rest = Sanitize(settings.Rest),
            Accessibility = settings.Accessibility ?? new AccessibilitySettings(),
            Ui = Sanitize(settings.Ui),
            Bulbs = Sanitize(settings.Bulbs),
            Songs = Sanitize(settings.Songs),
            Pictures = Sanitize(settings.Pictures),
            Files = settings.Files ?? new FileSettings(),
            Themes = Sanitize(settings.Themes),
            RecentSettings = Sanitize(settings.RecentSettings),
            Import54 = Sanitize(settings.Import54),
            Onboarding = Sanitize(settings.Onboarding),
        };

        // Every part comes back as the same instance when it was valid, so equal records mean nothing was repaired.
        return repaired == settings ? settings : repaired;
    }

    /// <summary>Category names trimmed, without empties and case-insensitive duplicates.</summary>
    /// <param name="categories">The names.</param>
    /// <returns>The same list when already clean.</returns>
    public static IReadOnlyList<string> CleanCategories(IReadOnlyList<string> categories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (categories.All(c => !string.IsNullOrWhiteSpace(c) && c == c.Trim() && seen.Add(c)))
        {
            return categories;
        }

        seen.Clear();
        return [.. categories.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Where(seen.Add)];
    }

    private static LightsSettings Sanitize(LightsSettings? lights)
    {
        if (lights is null)
        {
            return new LightsSettings();
        }

        LightsSettings repaired = lights with
        {
            Drawing = Defined(lights.Drawing, BulbDrawing.Desktop),
            Displays = Sanitize(lights.Displays),
            FrameMode = Defined(lights.FrameMode, FrameMode.EachDisplay),
            Size = Defined(lights.Size, BulbSize.Standard),
            PatternBeforeDance = lights.PatternBeforeDance is { } pattern && !Enum.IsDefined(pattern) ? null : lights.PatternBeforeDance,
        };
        return repaired == lights ? lights : repaired;
    }

    private static DisplaySelection Sanitize(DisplaySelection? displays)
    {
        if (displays is null)
        {
            return new DisplaySelection();
        }

        IReadOnlyList<string> disabled = ValueRules.CleanIdSet(displays.Disabled, static id => !string.IsNullOrWhiteSpace(id), StringComparer.OrdinalIgnoreCase);
        return ReferenceEquals(disabled, displays.Disabled) ? displays : new DisplaySelection { Disabled = disabled };
    }

    private static LookSettings Sanitize(LookSettings? look)
    {
        if (look is null)
        {
            return new LookSettings();
        }

        LookSettings repaired = look with { Pixels = Defined(look.Pixels, SpriteStyle.Smooth), Glow = Defined(look.Glow, GlowLevel.Soft) };
        return repaired == look ? look : repaired;
    }

    private static MusicSettings Sanitize(MusicSettings? music)
    {
        if (music is null)
        {
            return new MusicSettings();
        }

        MusicSettings repaired = music with
        {
            Volume = Math.Clamp(music.Volume, 0, 100),
            MidiDevice = music.MidiDevice ?? "",
            SyncOffsetMs = Math.Clamp(music.SyncOffsetMs, 0, MaxSyncOffsetMs),
        };
        return repaired == music ? music : repaired;
    }

    private static SaverDeviceSettings Sanitize(SaverDeviceSettings? saver)
    {
        if (saver is null)
        {
            return new SaverDeviceSettings();
        }

        SaverDeviceSettings repaired = saver with { ShowOn = Defined(saver.ShowOn, SaverDisplays.All) };
        return repaired == saver ? saver : repaired;
    }

    /// <summary>Exactly 16 custom colours: missing ones are black, extra ones are dropped.</summary>
    private static ColorSettings Sanitize(ColorSettings? colors)
    {
        if (colors is not null && colors.Custom is { Count: ColorSettings.CustomColorCount })
        {
            return colors;
        }

        RgbColor[] custom = Enumerable.Repeat(RgbColor.Black, ColorSettings.CustomColorCount).ToArray();
        colors?.Custom?.Take(ColorSettings.CustomColorCount).ToArray().CopyTo(custom, 0);
        return new ColorSettings { Custom = custom };
    }

    private static RestSettings Sanitize(RestSettings? rest)
    {
        if (rest is null)
        {
            return new RestSettings();
        }

        RestSettings repaired = rest with { EnergySaver = Defined(rest.EnergySaver, EnergySaverChoice.UseLessPower) };
        return repaired == rest ? rest : repaired;
    }

    private static UiSettings Sanitize(UiSettings? ui)
    {
        if (ui is null)
        {
            return new UiSettings();
        }

        UiSettings repaired = ui with { Settings = Sanitize(ui.Settings), Gallery = Sanitize(ui.Gallery) };
        return repaired == ui ? ui : repaired;
    }

    private static SettingsWindowSettings Sanitize(SettingsWindowSettings? window)
    {
        if (window is null)
        {
            return new SettingsWindowSettings();
        }

        SettingsWindowSettings repaired = window with
        {
            LastPage = Defined(window.LastPage, SettingsPageId.Home),
            Window = IsUsable(window.Window) ? window.Window : null,
        };
        return repaired == window ? window : repaired;
    }

    private static bool IsUsable(WindowPlacementSettings? placement) =>
        placement is null || (double.IsFinite(placement.Left) && double.IsFinite(placement.Top)
            && double.IsFinite(placement.Width) && double.IsFinite(placement.Height) && placement.Width > 0 && placement.Height > 0);

    private static GallerySettings Sanitize(GallerySettings? gallery)
    {
        if (gallery is null)
        {
            return new GallerySettings();
        }

        GallerySettings repaired = gallery with
        {
            View = Defined(gallery.View, BulbListView.Tiles),
            Sort = Defined(gallery.Sort, BulbSortOrder.Original),
        };
        return repaired == gallery ? gallery : repaired;
    }

    private static BulbPreferences Sanitize(BulbPreferences? bulbs)
    {
        if (bulbs is null)
        {
            return new BulbPreferences();
        }

        BulbPreferences repaired = bulbs with
        {
            Favorites = ValueRules.CleanIdSet(bulbs.Favorites, BulbIds.IsValid, BulbIds.Comparer),
            Hidden = ValueRules.CleanIdSet(bulbs.Hidden, BulbIds.IsValid, BulbIds.Comparer),
            CategoryOverrides = Sanitize(bulbs.CategoryOverrides),
        };
        return repaired == bulbs ? bulbs : repaired;
    }

    /// <summary>
    /// Overrides keyed case-insensitively by well-formed bulb ids (an empty dictionary may have any comparer); categories
    /// trimmed, non-empty and distinct.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Sanitize(IReadOnlyDictionary<string, IReadOnlyList<string>>? overrides)
    {
        if (overrides is { Count: 0 }
            || (overrides is Dictionary<string, IReadOnlyList<string>> dictionary
                && ReferenceEquals(dictionary.Comparer, BulbIds.Comparer)
                && dictionary.All(p => BulbIds.IsValid(p.Key) && p.Value is not null && ReferenceEquals(CleanCategories(p.Value), p.Value))))
        {
            return overrides;
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(BulbIds.Comparer);
        foreach ((string id, IReadOnlyList<string>? categories) in overrides ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            if (BulbIds.IsValid(id) && categories is not null)
            {
                result.TryAdd(id, CleanCategories(categories));
            }
        }

        return result;
    }

    private static HiddenItems Sanitize(HiddenItems? hidden)
    {
        if (hidden is null)
        {
            return new HiddenItems();
        }

        IReadOnlyList<string> items = ValueRules.CleanIdSet(hidden.Hidden, ValueRules.IsMediaId, MediaIds.Comparer);
        return ReferenceEquals(items, hidden.Hidden) ? hidden : new HiddenItems { Hidden = items };
    }

    private static ThemePreferences Sanitize(ThemePreferences? themes) =>
        themes is null ? new ThemePreferences()
        : themes.LastName is null ? themes with { LastName = "" }
        : themes;

    /// <summary>At most five entries with values; each entry's values repaired.</summary>
    private static IReadOnlyList<RecentSettingsEntry> Sanitize(IReadOnlyList<RecentSettingsEntry>? recent)
    {
        if (recent is null)
        {
            return [];
        }

        RecentSettingsEntry[] repaired = [.. recent.Where(e => e?.Values is not null).Take(MaxRecentSettings).Select(Sanitize)];
        return repaired.Length == recent.Count && repaired.Zip(recent).All(p => ReferenceEquals(p.First, p.Second)) ? recent : repaired;
    }

    private static RecentSettingsEntry Sanitize(RecentSettingsEntry entry)
    {
        ThemeableSettings values = ThemeableSanitizer.Sanitize(entry.Values);
        return entry.Label is not null && ReferenceEquals(values, entry.Values) ? entry : entry with { Label = entry.Label ?? "", Values = values };
    }

    private static Import54Record? Sanitize(Import54Record? record)
    {
        if (record is null || (record.Report is not null && record.Report.All(item => item?.Item is not null)))
        {
            return record;
        }

        return record with { Report = [.. (record.Report ?? []).Where(i => i is not null).Select(i => i.Item is null ? i with { Item = "" } : i)] };
    }

    private static OnboardingState Sanitize(OnboardingState? onboarding)
    {
        if (onboarding is null)
        {
            return new OnboardingState();
        }

        OnboardingState repaired = onboarding with
        {
            LocationHotKeyHints = Math.Max(0, onboarding.LocationHotKeyHints),
            LastNotified = onboarding.LastNotified ?? new Dictionary<string, DateOnly>(),
        };
        return repaired == onboarding ? onboarding : repaired;
    }

    private static T Defined<T>(T value, T fallback)
        where T : struct, Enum => Enum.IsDefined(value) ? value : fallback;
}
