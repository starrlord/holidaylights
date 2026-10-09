namespace HolidayLights.Core.Settings;

/// <summary>
/// Repairs the two hot keys: an unknown key becomes the default key, and a combination that would take a plain typing
/// key (no Ctrl, Alt or Windows on a key other than F1-F24 or Pause) gets the default modifiers, so a hand-edited file
/// can never make Holiday Lights swallow ordinary keystrokes (PRODUCT-SPEC 3.8.2).
/// </summary>
internal static class HotKeySanitizer
{
    private const HotKeyModifiers AllModifiers = HotKeyModifiers.Alt | HotKeyModifiers.Ctrl | HotKeyModifiers.Shift | HotKeyModifiers.Win;

    private const HotKeyModifiers CommandModifiers = HotKeyModifiers.Alt | HotKeyModifiers.Ctrl | HotKeyModifiers.Win;

    /// <summary>Repairs both bindings.</summary>
    /// <param name="hotKeys">The stored hot keys, possibly null when read from a file.</param>
    /// <returns>The same instance when valid, else a repaired copy.</returns>
    public static HotKeySettings Sanitize(HotKeySettings? hotKeys)
    {
        if (hotKeys is null)
        {
            return new HotKeySettings();
        }

        var defaults = new HotKeySettings();
        HotKeyBinding location = Sanitize(hotKeys.Location, defaults.Location);
        HotKeyBinding lights = Sanitize(hotKeys.Lights, defaults.Lights);
        return ReferenceEquals(location, hotKeys.Location) && ReferenceEquals(lights, hotKeys.Lights)
            ? hotKeys
            : new HotKeySettings { Location = location, Lights = lights };
    }

    private static HotKeyBinding Sanitize(HotKeyBinding? binding, HotKeyBinding defaultBinding)
    {
        if (binding is null)
        {
            return defaultBinding;
        }

        string key = HotKeyKeys.TryNormalize(binding.Key, out string? canonical) ? canonical : defaultBinding.Key;
        HotKeyModifiers modifiers = binding.Modifiers & AllModifiers;
        if ((modifiers & CommandModifiers) == HotKeyModifiers.None && !HotKeyKeys.MayOmitModifiers(key))
        {
            modifiers = defaultBinding.Modifiers;
        }

        return key == binding.Key && modifiers == binding.Modifiers ? binding : binding with { Key = key, Modifiers = modifiers };
    }
}
