using HolidayLights.Core.Themes;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The factory-default rule of PRODUCT-SPEC 2.5.2: every value of the 5.4 key equals its 5.4 default
/// (missing values count as defaults; <c>Path</c>, <c>Current Version</c>, <c>User</c>,
/// <c>Serial Number</c>, <c>Last Converted Picture *</c> and the derived "Enabled Music" are ignored), "Disabled Music"
/// and "Included Bulb Categories" are empty, and every theme is one of the 11 installer themes with its installer values.
/// True on the commissioning PC.
/// </summary>
internal static class LegacyFactoryDefaults
{
    private const uint ColorMask = 0x00FFFFFF;

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Path", "Current Version", "User", "Serial Number", "Last Converted Picture Name", "Last Converted Picture Time",
        LegacyValueNames.EnabledMusic,
    };

    /// <summary>Applies the rule.</summary>
    /// <param name="snapshot">The 5.4 registry.</param>
    /// <returns>True when 5.4 was at its factory defaults.</returns>
    public static bool IsFactoryDefault(LegacyRegistrySnapshot snapshot)
    {
        var main = new LegacyValueSet(snapshot.Values);
        return main.Names.Where(n => !Ignored.Contains(n)).All(name => IsDefault(main, name))
            && snapshot.IncludedBulbCategories.Count == 0
            && snapshot.Themes.All(theme => IsInstallerTheme(theme.Key, new LegacyValueSet(theme.Value)));
    }

    /// <summary>Unknown value names carry no choice 5.4 understood, so they do not count.</summary>
    private static bool IsDefault(LegacyValueSet main, string storedName)
    {
        string name = LegacyValueNames.Canonical(storedName);
        if (name.StartsWith("Custom Color  ", StringComparison.Ordinal))
        {
            return IsDword(main, name, 0, ColorMask);
        }

        return name switch
        {
            LegacyValueNames.BulbSettings => LegacyBulbSettings.Decode(main.Bytes(name)).SequenceEqual(LegacyBulbSettings.Default),
            LegacyValueNames.FlashInterval => IsDword(main, name, 5),
            LegacyValueNames.FlashPattern => IsDword(main, name, 1),
            LegacyValueNames.BulbLocation => IsDword(main, name, 0),
            LegacyValueNames.DisabledMusic => LegacyText.DecodeList(main.Bytes(name) ?? [0]).Count == 0,
            LegacyValueNames.MusicPlay => IsDword(main, name, 3),
            LegacyValueNames.SaverModule => main.Text(name, LegacyValueSet.PathBuffer) == SaverAnimations.Snow,
            LegacyValueNames.SaverMovementType => IsDword(main, name, (uint)SaverAnimations.DefaultStyleFor(SaverAnimations.Snow)),
            LegacyValueNames.SaverMessage => main.Text(name, LegacyValueSet.MessageBuffer) == new SaverLook().Message,
            LegacyValueNames.SaverMessageFont => LegacyFont.Decode(main.Bytes(name)) == new SaverFont(),
            LegacyValueNames.SaverMessageColor => IsDword(main, name, RgbColor.Red.ToColorRef(), ColorMask),
            LegacyValueNames.SaverBackgroundColor => IsDword(main, name, 0, ColorMask),
            LegacyValueNames.SaverPictureName => string.Equals(main.Text(name, LegacyValueSet.PathBuffer), "Santa Candle.BMP", StringComparison.OrdinalIgnoreCase),
            LegacyValueNames.SaverPictureDisplayType => IsDword(main, name, 0),
            LegacyValueNames.HotKeyOnOrOff => IsDword(main, name, 1),
            LegacyValueNames.HotKeyChar => IsDword(main, name, 'B'),
            LegacyValueNames.PreventSlowBulbWarning => IsDword(main, name, 0),
            _ => true,
        };
    }

    private static bool IsDword(LegacyValueSet main, string name, uint expected, uint mask = uint.MaxValue) =>
        (main.Dword(name) & mask) == expected;

    private static bool IsInstallerTheme(string name, LegacyValueSet values)
    {
        if (ShippedThemes.Find(name) is not { Shipped: ShippedThemeKind.Classic } original)
        {
            return false;
        }

        ThemeDefinition mapped = new LegacySettingsMapper(LegacyResolver.ForComparison).MapTheme(name, values, new LegacyMappingIssues());
        return ThemeValues.Equal(original, ThemeNormalizer.Normalize(mapped, name));
    }
}
