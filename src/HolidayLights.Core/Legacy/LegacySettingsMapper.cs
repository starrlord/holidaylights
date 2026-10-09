using HolidayLights.Core.Settings;
using HolidayLights.Core.Themes;

namespace HolidayLights.Core.Legacy;

/// <summary>Problems found while mapping 5.4 values (listed in the import report).</summary>
internal sealed class LegacyMappingIssues
{
    /// <summary>"Bulb Settings" ids that resolved to no installed bulb.</summary>
    public HashSet<int> UnresolvedBulbs { get; } = [];

    /// <summary>A "Screen Saver Module" that is neither a 5.4 animation nor a 5.4 add-on bulb's name (Snow is used).</summary>
    public bool UnknownAnimation { get; set; }
}

/// <summary>
/// Maps decoded 5.4 registry values to 6.0 values (PRODUCT-SPEC 6.8.2): the 13 theme values of the main key (missing
/// values are the 5.4 defaults) and of each theme (missing values stay missing and take their defaults when the theme is
/// loaded), and the device preferences (Bulb Drawing, hot key, custom colours, category overrides).
/// </summary>
internal sealed class LegacySettingsMapper
{
    private readonly LegacyResolver resolver;

    /// <summary>Creates the mapper.</summary>
    /// <param name="resolver">Turns 5.4 names and numbers into 6.0 ids.</param>
    public LegacySettingsMapper(LegacyResolver resolver) => this.resolver = resolver;

    /// <summary>The 13 current values of the main key.</summary>
    /// <param name="values">The main key.</param>
    /// <param name="issues">Receives the problems.</param>
    /// <returns>The values.</returns>
    public ThemeableSettings MapCurrent(LegacyValueSet values, LegacyMappingIssues issues)
    {
        ThemeDefinition asTheme = MapTheme("", values, issues);
        return new ThemeableSettings
        {
            Arrangement = asTheme.Arrangement ?? LegacyBulbSettings.ToArrangement(LegacyBulbSettings.Default, resolver.Bulb, issues.UnresolvedBulbs),
            Flash = new FlashSettings
            {
                Pattern = asTheme.Flash?.Pattern ?? FlashPatternId.FlashTogether,
                Interval = asTheme.Flash?.Interval ?? FlashSettings.DefaultInterval,
            },
            Music = new CurrentMusic { DisabledSongs = MapSongs(values.Bytes(LegacyValueNames.DisabledMusic)) ?? [], Mode = asTheme.Music?.Mode ?? PlayMode.Always },
            Saver = ThemeValues.EffectiveSaver(asTheme.Saver),
        };
    }

    /// <summary>A 5.4 theme (or the main key read as one).</summary>
    /// <param name="name">The theme name (valid).</param>
    /// <param name="values">The theme's key.</param>
    /// <param name="issues">Receives the problems.</param>
    /// <returns>The theme, not normalized.</returns>
    public ThemeDefinition MapTheme(string name, LegacyValueSet values, LegacyMappingIssues issues)
    {
        byte[]? bulbSettings = values.Bytes(LegacyValueNames.BulbSettings);
        uint? pattern = values.Dword(LegacyValueNames.FlashPattern);
        uint? interval = values.Dword(LegacyValueNames.FlashInterval);
        uint? mode = values.Dword(LegacyValueNames.MusicPlay);
        return new ThemeDefinition
        {
            Name = name,
            Arrangement = bulbSettings is null && !values.Contains(LegacyValueNames.BulbSettings)
                ? null
                : LegacyBulbSettings.ToArrangement(LegacyBulbSettings.Decode(bulbSettings), resolver.Bulb, issues.UnresolvedBulbs),
            Flash = pattern is null && interval is null ? null : new ThemeFlash
            {
                Pattern = pattern is null ? null : pattern <= (uint)FlashPatternId.RandomFlashing ? (FlashPatternId)pattern.Value : FlashPatternId.FlashTogether,
                Interval = interval is null ? null : ValueRules.NormalizeInterval((int)Math.Min(interval.Value, int.MaxValue)),
            },
            Music = new ThemeMusic
            {
                EnabledSongs = MapSongs(values.Bytes(LegacyValueNames.EnabledMusic)),
                Mode = mode is null ? null : mode <= (uint)PlayMode.Intermittently ? (PlayMode)mode.Value : PlayMode.Always,
            },
            Saver = MapSaver(values, issues),
        };
    }

    /// <summary>"Bulb Location": 0 is On Desktop (behind the icons), anything else On Top.</summary>
    /// <param name="values">The main key.</param>
    /// <returns>The Bulb Drawing.</returns>
    public static BulbDrawing MapDrawing(LegacyValueSet values) =>
        (values.Dword(LegacyValueNames.BulbLocation) ?? 0) == 0 ? BulbDrawing.Desktop : BulbDrawing.OnTop;

    /// <summary>The location hot key: the 5.4 switch and letter with Ctrl+Alt+Shift (PRODUCT-SPEC D16).</summary>
    /// <param name="values">The main key.</param>
    /// <param name="keyImported">False when the stored key is not one a hot key can use (the default letter is kept).</param>
    /// <returns>The binding.</returns>
    public static HotKeyBinding MapLocationHotKey(LegacyValueSet values, out bool keyImported)
    {
        var binding = new HotKeySettings().Location;
        keyImported = HotKeyKeys.TryFromVirtualKey(values.Dword(LegacyValueNames.HotKeyChar) ?? 'B', out string? key);
        return binding with
        {
            Enabled = (values.Dword(LegacyValueNames.HotKeyOnOrOff) ?? 1) != 0,
            Key = keyImported ? key! : binding.Key,
        };
    }

    /// <summary>The 16 custom colours ("Custom Color  0" to "Custom Color  15"; missing ones are black).</summary>
    /// <param name="values">The main key.</param>
    /// <returns>The colours.</returns>
    public static IReadOnlyList<RgbColor> MapCustomColors(LegacyValueSet values) =>
        [.. Enumerable.Range(0, ColorSettings.CustomColorCount).Select(i => RgbColor.FromColorRef(values.Dword(LegacyValueNames.CustomColor(i)) ?? 0))];

    /// <summary>"Included Bulb Categories": built-in bulb names to category overrides.</summary>
    /// <param name="categories">The raw values.</param>
    /// <param name="unknown">Receives the names that are no built-in bulb.</param>
    /// <returns>Overrides by bulb id.</returns>
    public static Dictionary<string, IReadOnlyList<string>> MapCategoryOverrides(IReadOnlyDictionary<string, string> categories, ICollection<string> unknown)
    {
        var overrides = new Dictionary<string, IReadOnlyList<string>>(BulbIds.Comparer);
        foreach ((string name, string list) in categories)
        {
            if (LegacyBuiltInBulbs.ByName(name) is { } id)
            {
                overrides[id] = SettingsSanitizer.CleanCategories(list.Split('|'));
            }
            else
            {
                unknown.Add(name);
            }
        }

        return overrides;
    }

    private IReadOnlyList<string>? MapSongs(byte[]? list) =>
        list is null ? null : [.. LegacyText.DecodeList(list).Select(resolver.Song).OfType<string>().Distinct(MediaIds.Comparer)];

    private ThemeSaver MapSaver(LegacyValueSet values, LegacyMappingIssues issues)
    {
        string? module = values.Text(LegacyValueNames.SaverModule, LegacyValueSet.PathBuffer);
        string? animation = module is null ? null : MapAnimation(module);
        issues.UnknownAnimation |= module is not null && animation is null;
        uint? style = values.Dword(LegacyValueNames.SaverMovementType);
        uint? color = values.Dword(LegacyValueNames.SaverMessageColor);
        uint? background = values.Dword(LegacyValueNames.SaverBackgroundColor);
        uint? placement = values.Dword(LegacyValueNames.SaverPictureDisplayType);
        string? message = values.Text(LegacyValueNames.SaverMessage, LegacyValueSet.MessageBuffer);
        string? picture = values.Text(LegacyValueNames.SaverPictureName, LegacyValueSet.PathBuffer);
        return new ThemeSaver
        {
            Animation = animation,
            Style = style <= (uint)SaverMovementStyle.Attraction ? (SaverMovementStyle)style.Value : null,
            Message = message is null ? null : ValueRules.CleanMessage(message),
            Font = values.Contains(LegacyValueNames.SaverMessageFont) ? LegacyFont.Decode(values.Bytes(LegacyValueNames.SaverMessageFont)) : null,
            Color = color is null ? null : RgbColor.FromColorRef(color.Value),
            Background = background is null ? null : RgbColor.FromColorRef(background.Value),
            Picture = picture is null ? null : picture == SaverPictures.None ? SaverPictures.None : resolver.Picture(picture) ?? SaverPictures.None,
            Placement = placement <= (uint)PicturePlacement.Stretch ? (PicturePlacement)placement.Value : placement is null ? null : PicturePlacement.Center,
        };
    }

    /// <summary>One of the 25 animations (exact, then ignoring case), else an add-on bulb of that name, else unknown.</summary>
    private string? MapAnimation(string module) =>
        SaverAnimations.All.FirstOrDefault(a => string.Equals(a, module, StringComparison.Ordinal))
        ?? (resolver.AddOnByName(module) is { } bulbId ? SaverAnimations.ForBulb(bulbId) : null)
        ?? SaverAnimations.All.FirstOrDefault(a => string.Equals(a, module, StringComparison.OrdinalIgnoreCase));
}
