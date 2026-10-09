using System.Globalization;
using System.IO;
using HolidayLights.App.ScreenSaver;

namespace HolidayLights.App.Shell;

/// <summary>Parses command lines, including the 5.4 forms and the screen saver arguments (case-insensitive, '-' for '/'). Owner: app-shell.</summary>
/// <remarks>
/// <para>Recognized forms (PRODUCT-SPEC 6.6.3, 6.2.1): <c>--data-root &lt;dir&gt;</c> and <c>--no-system-changes</c>
/// anywhere; then one command: the screen saver switches as <see cref="ScreenSaverArguments"/> reads them (<c>/s</c>,
/// <c>/p &lt;hwnd&gt;</c>, <c>/p:&lt;hwnd&gt;</c>, <c>/c</c>, <c>/c:&lt;hwnd&gt;</c>, <c>/a</c>); <c>--autostart</c>, <c>--settings [page]</c>, <c>--open &lt;files&gt;</c>, <c>--toggle-layer</c>,
/// <c>--lights on|off|toggle</c>, <c>--theme &lt;name&gt;</c>, <c>--exit</c>, <c>--reset</c>,
/// <c>--render-test &lt;dir&gt;</c>, <c>--install</c>, <c>--uninstall</c>, <c>--diagnostics [file]</c>; the 5.4 forms
/// <c>settings</c>, <c>reset</c> and <c>open &lt;file&gt;</c> (everything after "open " is the path); and full paths
/// alone (files dropped on the program).</para>
/// <para>A <c>.scr</c> copy started without arguments is Windows' screen saver "Settings" button (configure); a copy
/// named <c>Setup.exe</c> started without arguments is the installer.</para>
/// </remarks>
public static class CommandLine
{
    /// <summary>The file name (without extension) of the installer copy of the program in the distribution folder.</summary>
    public const string SetupProgramName = "Setup";

    /// <summary>Parses the arguments of <see cref="Program.Main"/>.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The request; unknown arguments give <see cref="LaunchKind.Normal"/> and are logged.</returns>
    public static LaunchRequest Parse(IReadOnlyList<string> args) => Parse(args, Environment.ProcessPath);

    /// <summary>Parses arguments knowing the path of the running program (a <c>.scr</c> or <c>Setup.exe</c> copy changes the meaning of an empty command line).</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="processPath">The path of the program file, or null when unknown.</param>
    /// <returns>The request.</returns>
    public static LaunchRequest Parse(IReadOnlyList<string> args, string? processPath)
    {
        ArgumentNullException.ThrowIfNull(args);
        var unknown = new List<string>();
        var command = new List<string>();
        string? dataRoot = null;
        bool noSystemChanges = false;

        for (int i = 0; i < args.Count; i++)
        {
            string argument = args[i].Trim();
            if (IsOption(argument, "--data-root"))
            {
                if (i + 1 < args.Count && !IsModernOption(args[i + 1]))
                {
                    dataRoot = FullPath(args[++i].Trim());
                }
                else
                {
                    unknown.Add(argument);
                }
            }
            else if (IsOption(argument, "--no-system-changes"))
            {
                noSystemChanges = true;
            }
            else if (argument.Length > 0)
            {
                command.Add(argument);
            }
        }

        LaunchRequest request = ParseCommand(command, processPath, unknown);
        return request with { DataRoot = dataRoot, NoSystemChanges = noSystemChanges, UnknownArguments = unknown };
    }

    private static LaunchRequest ParseCommand(List<string> command, string? processPath, List<string> unknown)
    {
        bool screenSaverCopy = string.Equals(Path.GetExtension(processPath ?? ""), ".scr", StringComparison.OrdinalIgnoreCase);
        if (ScreenSaverArguments.TryParse(command, screenSaverCopy, out ScreenSaverInvocation? saver))
        {
            unknown.AddRange(command.Skip(ScreenSaverArgumentCount(command, saver.Mode)));
            return new LaunchRequest { Kind = KindOf(saver.Mode), WindowHandle = saver.WindowHandle };
        }

        if (command.Count == 0)
        {
            string name = Path.GetFileNameWithoutExtension(processPath ?? "");
            return string.Equals(name, SetupProgramName, StringComparison.OrdinalIgnoreCase)
                ? new LaunchRequest { Kind = LaunchKind.Install }
                : new LaunchRequest();
        }

        return command[0].StartsWith("--", StringComparison.Ordinal) ? ParseModern(command, unknown) : ParseLegacy(command, unknown);
    }

    private static LaunchKind KindOf(ScreenSaverMode mode) => mode switch
    {
        ScreenSaverMode.Show => LaunchKind.ScreenSaverShow,
        ScreenSaverMode.Preview => LaunchKind.ScreenSaverPreview,
        ScreenSaverMode.Configure => LaunchKind.ScreenSaverConfigure,
        _ => LaunchKind.ScreenSaverPassword,
    };

    /// <summary>How many arguments a recognised screen saver switch used: the switch, and the window handle when it is the next argument ("/p 1234").</summary>
    private static int ScreenSaverArgumentCount(List<string> command, ScreenSaverMode mode)
    {
        if (command.Count == 0)
        {
            return 0;
        }

        bool handleFollows = mode != ScreenSaverMode.Show && command[0].Trim().Length == 2 && command.Count > 1
            && long.TryParse(command[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _);
        return handleFollows ? 2 : 1;
    }

    private static LaunchRequest ParseModern(List<string> command, List<string> unknown)
    {
        string option = command[0];
        List<string> values = [.. command.Skip(1).TakeWhile(a => !IsModernOption(a))];
        List<string> extra = [.. command.Skip(1 + values.Count)];
        LaunchRequest request;

        switch (option.ToLowerInvariant())
        {
            case "--autostart":
                request = new LaunchRequest { Kind = LaunchKind.Autostart };
                break;
            case "--settings":
                request = ParseSettings(values, unknown);
                values.Clear();
                break;
            case "--open":
                request = values.Count == 0
                    ? Unknown(option, unknown)
                    : new LaunchRequest { Kind = LaunchKind.Open, Files = [.. values.Select(FullPath)] };
                values.Clear();
                break;
            case "--toggle-layer":
                request = new LaunchRequest { Kind = LaunchKind.ToggleLayer };
                break;
            case "--lights":
                request = ParseLights(option, values, unknown);
                values.Clear();
                break;
            case "--theme":
                request = values.Count == 0
                    ? Unknown(option, unknown)
                    : new LaunchRequest { Kind = LaunchKind.Theme, Argument = string.Join(' ', values) };
                values.Clear();
                break;
            case "--exit":
                request = new LaunchRequest { Kind = LaunchKind.Exit };
                break;
            case "--reset":
                request = new LaunchRequest { Kind = LaunchKind.Reset };
                break;
            case "--render-test":
                request = new LaunchRequest { Kind = LaunchKind.RenderTest, Argument = FullPath(values.FirstOrDefault() ?? ".") };
                values = [.. values.Skip(1)];
                break;
            case "--install":
                request = new LaunchRequest { Kind = LaunchKind.Install };
                break;
            case "--uninstall":
                request = new LaunchRequest { Kind = LaunchKind.Uninstall };
                break;
            case "--diagnostics":
                request = new LaunchRequest { Kind = LaunchKind.Diagnostics, Argument = values.Count > 0 ? FullPath(values[0]) : null };
                values = [.. values.Skip(1)];
                break;
            default:
                request = Unknown(option, unknown);
                break;
        }

        unknown.AddRange(values);
        unknown.AddRange(extra);
        return request;
    }

    private static LaunchRequest ParseSettings(List<string> values, List<string> unknown)
    {
        if (values.Count == 0)
        {
            return new LaunchRequest { Kind = LaunchKind.Settings };
        }

        if (InstanceCommand.TryParsePageName(values[0], out SettingsPageId page))
        {
            unknown.AddRange(values.Skip(1));
            return new LaunchRequest { Kind = LaunchKind.Settings, Page = page };
        }

        unknown.AddRange(values);
        return new LaunchRequest { Kind = LaunchKind.Settings };
    }

    private static LaunchRequest ParseLights(string option, List<string> values, List<string> unknown)
    {
        string state = values.Count == 0 ? "toggle" : values[0].ToLowerInvariant();
        if (state is not ("on" or "off" or "toggle"))
        {
            unknown.AddRange(values);
            return Unknown(option, unknown);
        }

        unknown.AddRange(values.Skip(1));
        return new LaunchRequest { Kind = LaunchKind.Lights, Argument = state };
    }

    private static LaunchRequest ParseLegacy(List<string> command, List<string> unknown)
    {
        string first = command[0];
        if (command.Count == 1 && string.Equals(first, "settings", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchRequest { Kind = LaunchKind.LegacySettings };
        }

        if (command.Count == 1 && string.Equals(first, "reset", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchRequest { Kind = LaunchKind.Reset };
        }

        if (string.Equals(first, "open", StringComparison.OrdinalIgnoreCase) && command.Count > 1)
        {
            // 5.4 wrote the association as `"<exe>" open %1` with an unquoted %1: everything after "open " is one path.
            string path = string.Join(' ', command.Skip(1)).Trim('"');
            return new LaunchRequest { Kind = LaunchKind.Open, Files = [FullPath(path)] };
        }

        if (command.All(IsDroppedFile))
        {
            return new LaunchRequest { Kind = LaunchKind.Open, Files = [.. command.Select(FullPath)] };
        }

        unknown.AddRange(command);
        return new LaunchRequest();
    }

    private static LaunchRequest Unknown(string option, List<string> unknown)
    {
        unknown.Add(option);
        return new LaunchRequest();
    }

    private static bool IsDroppedFile(string argument) =>
        argument[0] is not ('-' or '/') && Path.IsPathFullyQualified(argument);

    private static bool IsOption(string argument, string option) => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase);

    private static bool IsModernOption(string argument) => argument.TrimStart().StartsWith("--", StringComparison.Ordinal);

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
