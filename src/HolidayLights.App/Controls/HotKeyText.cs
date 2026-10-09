using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// Converts between WPF keys and hot-key bindings and formats them as key caps (PRODUCT-SPEC 3.7, 3.8.2). The accepted
/// keys are A-Z, 0-9 (top row and numeric keypad), F1-F24, Insert, Home, End, Page Up, Page Down and Pause.
/// </summary>
public static class HotKeyText
{
    /// <summary>The key name a WPF key is stored as (<see cref="HotKeyBinding.Key"/>), or null when it cannot be a hot key.</summary>
    /// <param name="key">The pressed key (for Alt combinations: <c>KeyEventArgs.SystemKey</c>).</param>
    /// <returns>"A"-"Z", "0"-"9", "NumPad0"-"NumPad9", "F1"-"F24", "Insert", "Home", "End", "PageUp", "PageDown", "Pause", or null.</returns>
    public static string? ToBindingKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1),
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Pause => "Pause",
        _ => null,
    };

    /// <summary>True for the modifier keys themselves (they never end a recording).</summary>
    /// <param name="key">The key.</param>
    /// <returns>True for Ctrl, Alt, Shift and Windows.</returns>
    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>Converts WPF modifier keys.</summary>
    /// <param name="modifiers">The pressed modifiers.</param>
    /// <returns>The hot-key modifiers.</returns>
    public static HotKeyModifiers ToBindingModifiers(ModifierKeys modifiers)
    {
        HotKeyModifiers result = HotKeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HotKeyModifiers.Ctrl;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HotKeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HotKeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HotKeyModifiers.Win;
        }

        return result;
    }

    /// <summary>The caps of a combination in display order: "Ctrl", "Alt", "Shift", "Win", then the key ("Page Up", "Num 5").</summary>
    /// <param name="modifiers">The modifiers.</param>
    /// <param name="key">The binding key, or null while only modifiers are held.</param>
    /// <returns>The caps.</returns>
    public static IReadOnlyList<string> Caps(HotKeyModifiers modifiers, string? key)
    {
        var caps = new List<string>(5);
        if (modifiers.HasFlag(HotKeyModifiers.Ctrl))
        {
            caps.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            caps.Add("Alt");
        }

        if (modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            caps.Add("Shift");
        }

        if (modifiers.HasFlag(HotKeyModifiers.Win))
        {
            caps.Add("Win");
        }

        if (!string.IsNullOrEmpty(key))
        {
            caps.Add(DisplayKey(key));
        }

        return caps;
    }

    /// <summary>The caps of a binding.</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The caps.</returns>
    public static IReadOnlyList<string> Caps(HotKeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Caps(binding.Modifiers, binding.Key);
    }

    /// <summary>The written form, "Ctrl+Alt+Shift+B" (pill, InfoBars, the "Where Bulbs Are Drawn" hot key line).</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The text.</returns>
    public static string Compact(HotKeyBinding binding) => string.Join("+", Caps(binding));

    /// <summary>The spoken form, "Ctrl Alt Shift B" (the Change Hot Key announcement).</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The text.</returns>
    public static string Spoken(HotKeyBinding binding) => string.Join(" ", Caps(binding));

    /// <summary>How a binding key is shown on its cap.</summary>
    /// <param name="key">A binding key.</param>
    /// <returns>E.g. "Page Up", "Num 5", "B".</returns>
    public static string DisplayKey(string key) => key switch
    {
        "PageUp" => "Page Up",
        "PageDown" => "Page Down",
        _ when key.StartsWith("NumPad", StringComparison.Ordinal) => "Num " + key["NumPad".Length..],
        _ => key,
    };
}
