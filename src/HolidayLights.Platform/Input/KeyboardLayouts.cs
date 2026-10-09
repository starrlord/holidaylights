using System.Globalization;
using HolidayLights.Platform.Native;
using Microsoft.Win32;

namespace HolidayLights.Platform.Input;

/// <summary>
/// The installed keyboard layouts (<c>GetKeyboardLayoutList</c>) and the AltGr check of PRODUCT-SPEC 3.8.2: does a Ctrl+Alt
/// combination type a character on any of them (<c>ToUnicodeEx</c> without changing the keyboard state)?
/// </summary>
internal static unsafe class KeyboardLayouts
{
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftShift = 0xA0;
    private const int VkLeftControl = 0xA2;
    private const int VkRightMenu = 0xA5;
    private const byte KeyDown = 0x80;

    /// <summary>The input locale identifiers (HKLs) loaded for the desktop, sorted (a stable signature for change checks).</summary>
    /// <returns>The layouts.</returns>
    public static IReadOnlyList<nint> Installed()
    {
        int count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return [];
        }

        var layouts = new nint[count];
        fixed (nint* buffer = layouts)
        {
            count = NativeMethods.GetKeyboardLayoutList(count, buffer);
        }

        return [.. layouts.Take(Math.Max(0, count)).Order()];
    }

    /// <summary>Finds the first installed layout on which the combination types a character with AltGr.</summary>
    /// <param name="virtualKey">The key.</param>
    /// <param name="modifiers">The modifiers (Ctrl and Alt are assumed; Shift is honoured).</param>
    /// <returns>The character and layout, or null when no layout types anything.</returns>
    public static AltGrCharacter? FindAltGrCharacter(uint virtualKey, HotKeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(HotKeyModifiers.Shift);
        foreach (nint layout in Installed())
        {
            if (TypedText(layout, virtualKey, shift) is { } text)
            {
                return new AltGrCharacter(text, DisplayName(layout));
            }
        }

        return null;
    }

    /// <summary>The text a key types on a layout with Ctrl+Alt (= AltGr) held, or null when it types nothing printable.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="virtualKey">The key.</param>
    /// <param name="shift">Shift is held too.</param>
    /// <returns>The printable text, or null.</returns>
    public static string? TypedText(nint layout, uint virtualKey, bool shift)
    {
        byte* state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        state[VkControl] = state[VkLeftControl] = KeyDown;
        state[VkMenu] = state[VkRightMenu] = KeyDown;
        if (shift)
        {
            state[VkShift] = state[VkLeftShift] = KeyDown;
        }

        char* buffer = stackalloc char[8];
        uint scanCode = NativeMethods.MapVirtualKeyEx(virtualKey, NativeMethods.MAPVK_VK_TO_VSC, layout);
        int length = NativeMethods.ToUnicodeEx(virtualKey, scanCode, state, buffer, 8, NativeMethods.TOUNICODE_NO_STATE_CHANGE, layout);

        // A negative length is a dead key: buffer[0] holds its spacing character.
        string text = length switch
        {
            > 0 => new string(buffer, 0, Math.Min(length, 8)),
            < 0 => new string(buffer[0], 1),
            _ => "",
        };
        return text.Any(c => !char.IsControl(c)) ? text : null;
    }

    /// <summary>The layout's display name ("Polish (Programmers)"), from the registry (read-only).</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The name, or the language name when the layout is not described.</returns>
    public static string DisplayName(nint layout)
    {
        string? layoutId = LayoutId(layout);
        if (layoutId is not null)
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{LayoutsKey}\{layoutId}");
            if (key?.GetValue("Layout Display Name") is string indirect && LoadIndirectString(indirect) is { Length: > 0 } name)
            {
                return name;
            }

            if (key?.GetValue("Layout Text") is string text && text.Length > 0)
            {
                return text;
            }
        }

        int language = (int)((ulong)layout & 0xFFFF);
        try
        {
            return CultureInfo.GetCultureInfo(language).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return language.ToString("X4", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The keyboard layout identifier (KLID, the registry key name) of an HKL: <c>0x04150415</c> is <c>00000415</c>; a
    /// device word <c>0xFnnn</c> names a layout variant by its "Layout Id"; IMEs (<c>0xE...</c>) use the whole HKL.
    /// </summary>
    /// <param name="layout">The HKL.</param>
    /// <returns>The KLID, or null when a variant is not listed.</returns>
    public static string? LayoutId(nint layout)
    {
        uint value = (uint)((ulong)layout & 0xFFFFFFFF);
        uint device = value >> 16;
        switch (device & 0xF000)
        {
            case 0xF000:
                string wanted = (device & 0x0FFF).ToString("X4", CultureInfo.InvariantCulture);
                using (RegistryKey? layouts = Registry.LocalMachine.OpenSubKey(LayoutsKey))
                {
                    foreach (string name in layouts?.GetSubKeyNames() ?? [])
                    {
                        using RegistryKey? candidate = layouts!.OpenSubKey(name);
                        if (candidate?.GetValue("Layout Id") is string id && string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            return name;
                        }
                    }
                }

                return null;
            case 0xE000:
                return value.ToString("X8", CultureInfo.InvariantCulture);
            default:
                return device.ToString("X8", CultureInfo.InvariantCulture);
        }
    }

    private static string? LoadIndirectString(string source)
    {
        char* buffer = stackalloc char[256];
        return NativeMethods.SHLoadIndirectString(source, buffer, 256, 0) == 0 ? new string(buffer) : null;
    }
}
