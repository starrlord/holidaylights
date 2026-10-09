namespace HolidayLights.Core.Settings;

/// <summary>
/// The keys a hot key may use (PRODUCT-SPEC 3.8.2: A-Z, 0-9, the numeric keypad digits, F1-F24, Insert, Home, End,
/// Page Up, Page Down, Pause) with their names in <see cref="HotKeyBinding.Key"/> and their Win32 virtual-key codes.
/// </summary>
internal static class HotKeyKeys
{
    private static readonly Dictionary<string, uint> VirtualKeys = Build();

    private static readonly Dictionary<uint, string> Names = VirtualKeys.ToDictionary(p => p.Value, p => p.Key);

    /// <summary>Returns the canonical spelling of a key name ("b" gives "B", "pageup" gives "PageUp").</summary>
    /// <param name="key">A stored key name.</param>
    /// <param name="canonical">The canonical name when the key is accepted.</param>
    /// <returns>True for an accepted key.</returns>
    public static bool TryNormalize(string? key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? canonical)
    {
        if (key is not null && VirtualKeys.TryGetValue(key.Trim(), out uint virtualKey))
        {
            canonical = Names[virtualKey];
            return true;
        }

        canonical = null;
        return false;
    }

    /// <summary>Converts a Win32 virtual-key code (5.4 "Bulb Location Hot Key Char") into a key name.</summary>
    /// <param name="virtualKey">The virtual-key code.</param>
    /// <param name="name">The key name when the key is accepted.</param>
    /// <returns>True for an accepted key.</returns>
    public static bool TryFromVirtualKey(uint virtualKey, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name) =>
        Names.TryGetValue(virtualKey, out name);

    /// <summary>True for the keys that may be a hot key without Ctrl, Alt or Windows (F1-F24 and Pause).</summary>
    /// <param name="key">A canonical key name.</param>
    /// <returns>True when the key may stand alone.</returns>
    public static bool MayOmitModifiers(string key) =>
        key == "Pause" || (key.Length > 1 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int number) && number is >= 1 and <= 24);

    private static Dictionary<string, uint> Build()
    {
        var keys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            keys.Add(letter.ToString(), letter);
        }

        for (int digit = 0; digit <= 9; digit++)
        {
            keys.Add(digit.ToString(System.Globalization.CultureInfo.InvariantCulture), (uint)('0' + digit));
            keys.Add($"NumPad{digit}", (uint)(0x60 + digit));
        }

        for (int number = 1; number <= 24; number++)
        {
            keys.Add($"F{number}", (uint)(0x6F + number));
        }

        keys.Add("Insert", 0x2D);
        keys.Add("Home", 0x24);
        keys.Add("End", 0x23);
        keys.Add("PageUp", 0x21);
        keys.Add("PageDown", 0x22);
        keys.Add("Pause", 0x13);
        return keys;
    }
}
