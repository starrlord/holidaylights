namespace HolidayLights.Platform.Input;

/// <summary>A character that a combination types through AltGr on an installed keyboard layout.</summary>
/// <param name="Character">The character(s) produced, e.g. "Ł".</param>
/// <param name="LayoutName">The layout's display name, e.g. "Polish (Programmers)".</param>
internal readonly record struct AltGrCharacter(string Character, string LayoutName);

/// <summary>
/// The message rules of the Change Hot Key dialog (PRODUCT-SPEC 3.8.2), first that applies: a key that is not accepted,
/// Shift plus a letter alone, a missing Ctrl/Alt/Windows (except F1-F24 and Pause), in use by another program, typed with
/// AltGr on an installed layout (refusals), then the browser-shortcut and Windows-key warnings. Pure: the two system
/// queries are passed in.
/// </summary>
internal static class HotKeyRules
{
    private const HotKeyModifiers AllModifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift | HotKeyModifiers.Win;

    /// <summary>Ctrl+Shift+B (bookmarks bar), T, N and Delete: the browser shortcuts Holiday Lights would take away.</summary>
    private static readonly HashSet<string> BrowserShortcutKeys = new(StringComparer.OrdinalIgnoreCase) { "B", "T", "N", "Delete" };

    /// <summary>Applies every rule in order.</summary>
    /// <param name="binding">The candidate combination.</param>
    /// <param name="isInUse">Tries the combination with <c>RegisterHotKey</c> (gets the virtual-key code).</param>
    /// <param name="findAltGrCharacter">Looks for a character the combination types with AltGr (gets the virtual-key code and modifiers).</param>
    /// <returns>The verdict.</returns>
    public static HotKeyCheck Validate(
        HotKeyBinding binding, Func<uint, bool> isInUse, Func<uint, HotKeyModifiers, AltGrCharacter?> findAltGrCharacter)
    {
        if (CheckShape(binding, out uint virtualKey) is { } refusal)
        {
            return new HotKeyCheck(refusal);
        }

        if (isInUse(virtualKey))
        {
            return new HotKeyCheck(HotKeyValidity.InUse);
        }

        if (UsesAltGr(binding.Modifiers) && findAltGrCharacter(virtualKey, binding.Modifiers) is { } typed)
        {
            return new HotKeyCheck(HotKeyValidity.TypesCharacter, typed.Character, typed.LayoutName);
        }

        HotKeyModifiers modifiers = binding.Modifiers & AllModifiers;
        if (modifiers == (HotKeyModifiers.Ctrl | HotKeyModifiers.Shift) && BrowserShortcutKeys.Contains(binding.Key))
        {
            return new HotKeyCheck(HotKeyValidity.BrowserShortcut);
        }

        return new HotKeyCheck(modifiers.HasFlag(HotKeyModifiers.Win) ? HotKeyValidity.WindowsKeyCombination : HotKeyValidity.Ok);
    }

    /// <summary>The rules that need no system query: accepted key, Shift plus a letter, a required modifier.</summary>
    /// <param name="binding">The combination.</param>
    /// <param name="virtualKey">The virtual-key code (when the key is accepted).</param>
    /// <returns>The refusal, or null when the shape is fine.</returns>
    public static HotKeyValidity? CheckShape(HotKeyBinding binding, out uint virtualKey)
    {
        if (!HotKeyKeys.TryGetVirtualKey(binding.Key, out virtualKey))
        {
            return HotKeyValidity.KeyNotAllowed;
        }

        HotKeyModifiers modifiers = binding.Modifiers & AllModifiers;
        if (modifiers == HotKeyModifiers.Shift && HotKeyKeys.IsLetter(binding.Key))
        {
            return HotKeyValidity.ShiftLetterOnly;
        }

        if ((modifiers & (HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Win)) == 0 && !HotKeyKeys.WorksWithoutModifier(binding.Key))
        {
            return HotKeyValidity.NeedsModifier;
        }

        return null;
    }

    /// <summary>True when the combination holds Ctrl+Alt without Windows: Windows treats it as AltGr, so it can steal a typed character.</summary>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>True when the AltGr check applies.</returns>
    public static bool UsesAltGr(HotKeyModifiers modifiers) =>
        modifiers.HasFlag(HotKeyModifiers.Ctrl) && modifiers.HasFlag(HotKeyModifiers.Alt) && !modifiers.HasFlag(HotKeyModifiers.Win);
}
