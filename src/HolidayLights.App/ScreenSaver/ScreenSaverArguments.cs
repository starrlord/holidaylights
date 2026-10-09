using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace HolidayLights.App.ScreenSaver;

/// <summary>What Windows asks a screen saver to do (PRODUCT-SPEC 6.2.1).</summary>
public enum ScreenSaverMode
{
    /// <summary><c>/s</c>: run full screen.</summary>
    Show,

    /// <summary><c>/p &lt;hwnd&gt;</c>, <c>/p:&lt;hwnd&gt;</c> (also <c>/l</c>): draw inside Windows' preview window.</summary>
    Preview,

    /// <summary><c>/c</c>, <c>/c:&lt;hwnd&gt;</c>, or a <c>.scr</c> started without arguments: open the screen saver settings.</summary>
    Configure,

    /// <summary><c>/a</c>: the Windows 9x password change; ignored.</summary>
    ChangePassword,
}

/// <summary>A screen saver invocation.</summary>
/// <param name="Mode">What to do.</param>
/// <param name="WindowHandle">The window of <c>/p</c> (the preview parent) or <c>/c:</c> and <c>/a</c> (the owner); 0 when none was given.</param>
public sealed record ScreenSaverInvocation(ScreenSaverMode Mode, nint WindowHandle);

/// <summary>
/// The screen saver forms of the command line, as Windows passes them (<c>/S</c> from the shell, <c>/p 1234</c> from the
/// Screen Saver Settings dialog, <c>/c:1234</c> for "Settings", nothing for the <c>.scr</c> "Configure" verb). The switch
/// letter is case-insensitive and may follow '/' or '-' (scrnsave.lib); the window handle is decimal, after ':', directly
/// after the letter, or as the next argument.
/// </summary>
public static class ScreenSaverArguments
{
    /// <summary>Recognises a screen saver invocation.</summary>
    /// <param name="args">The command-line arguments (without the program name).</param>
    /// <param name="launchedAsScreenSaver">
    /// True when the program runs as <c>Holiday Lights.scr</c>: then no arguments mean "Configure" (Windows' Configure verb);
    /// <c>HolidayLights.exe</c> without arguments is a normal start.
    /// </param>
    /// <param name="invocation">The invocation when recognised.</param>
    /// <returns>True for a screen saver invocation; false for any other command line (Holiday Lights' own options).</returns>
    public static bool TryParse(IReadOnlyList<string> args, bool launchedAsScreenSaver, [NotNullWhen(true)] out ScreenSaverInvocation? invocation)
    {
        ArgumentNullException.ThrowIfNull(args);
        invocation = null;
        if (args.Count == 0)
        {
            invocation = launchedAsScreenSaver ? new ScreenSaverInvocation(ScreenSaverMode.Configure, 0) : null;
            return invocation is not null;
        }

        string first = args[0].Trim();
        if (first.Length < 2 || first[0] is not ('/' or '-'))
        {
            return false;
        }

        string rest = first[2..];
        string? next = args.Count > 1 ? args[1] : null;
        ScreenSaverMode? mode = char.ToLowerInvariant(first[1]) switch
        {
            's' when rest.Length == 0 => ScreenSaverMode.Show,
            'p' or 'l' => ScreenSaverMode.Preview,
            'c' => ScreenSaverMode.Configure,
            'a' => ScreenSaverMode.ChangePassword,
            _ => null,
        };
        nint handle = 0;
        if (mode is not { } recognised || (recognised != ScreenSaverMode.Show && !TryReadHandle(rest, next, out handle)))
        {
            return false;
        }

        invocation = new ScreenSaverInvocation(recognised, handle);
        return true;
    }

    /// <summary>
    /// Reads the window handle after the switch letter (":1234" or "1234"), else from the next argument. An absent handle
    /// is 0; text that is not a handle after the letter ("/sound") means the argument is not a screen saver switch.
    /// </summary>
    private static bool TryReadHandle(string rest, string? next, out nint handle)
    {
        handle = 0;
        string attached = rest.StartsWith(':') ? rest[1..] : rest;
        if (attached.Length > 0)
        {
            return TryParseHandle(attached, out handle);
        }

        if (next is not null && TryParseHandle(next.Trim(), out nint fromNext))
        {
            handle = fromNext;
        }

        return true;
    }

    private static bool TryParseHandle(string text, out nint handle)
    {
        bool parsed = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value);
        handle = parsed ? (nint)value : 0;
        return parsed;
    }
}
