using System.Globalization;

namespace HolidayLights.Platform.Input;

/// <summary>
/// The keys a hot key may use (PRODUCT-SPEC 3.8.2, <see cref="HotKeyBinding.Key"/>): "A"-"Z", "0"-"9", "NumPad0"-"NumPad9",
/// "F1"-"F24", "Insert", "Home", "End", "PageUp", "PageDown" and "Pause", with their virtual-key codes. Names are
/// case-insensitive.
/// </summary>
internal static class HotKeyKeys
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Pause"] = 0x13,
    };

    /// <summary>Maps a key name to its virtual-key code.</summary>
    /// <param name="key">The key name.</param>
    /// <param name="virtualKey">The virtual-key code when the key is accepted.</param>
    /// <returns>True when the key may be used in a hot key.</returns>
    public static bool TryGetVirtualKey(string? key, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }

            return false;
        }

        if (NamedKeys.TryGetValue(key, out virtualKey))
        {
            return true;
        }

        if (TryParseNumbered(key, "NumPad", 0, 9, out int digit))
        {
            virtualKey = 0x60 + (uint)digit;
            return true;
        }

        if (TryParseNumbered(key, "F", 1, 24, out int function))
        {
            virtualKey = 0x70 + (uint)function - 1;
            return true;
        }

        return false;
    }

    /// <summary>True for "A"-"Z".</summary>
    /// <param name="key">The key name.</param>
    /// <returns>True when the key is a letter.</returns>
    public static bool IsLetter(string key) => key.Length == 1 && char.ToUpperInvariant(key[0]) is >= 'A' and <= 'Z';

    /// <summary>True for the keys that may be used without Ctrl, Alt or Windows: F1-F24 and Pause.</summary>
    /// <param name="key">The key name.</param>
    /// <returns>True when no modifier is needed.</returns>
    public static bool WorksWithoutModifier(string key) =>
        string.Equals(key, "Pause", StringComparison.OrdinalIgnoreCase) || TryParseNumbered(key, "F", 1, 24, out _);

    private static bool TryParseNumbered(string key, string prefix, int min, int max, out int number)
    {
        number = 0;
        return key.Length > prefix.Length &&
               key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
               number >= min && number <= max &&
               key.Length - prefix.Length == number.ToString(CultureInfo.InvariantCulture).Length;
    }
}
