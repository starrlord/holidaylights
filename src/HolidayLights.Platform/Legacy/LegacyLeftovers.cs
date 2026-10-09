using System.Security;
using Microsoft.Win32;

namespace HolidayLights.Platform.Legacy;

/// <summary>Detects and fixes Holiday Lights 5.4 leftovers (see <see cref="ILegacyLeftovers"/>). Owner: core-settings.</summary>
/// <remarks>
/// A Startup shortcut counts as 5.4's when it matches <c>*Holiday Lights*.lnk</c> and targets the 5.4 program: a file
/// named "Holiday Lights.exe" (8.3 paths expanded) or the program named by 5.4's <c>Path</c> values. With
/// <see cref="AppRuntimeOptions.AllowSystemChanges"/> false the fixes only log what they would have done.
/// </remarks>
public sealed class LegacyLeftovers : ILegacyLeftovers
{
    private const string LogSource = "Legacy";
    private const string ProgramFileName = "Holiday Lights.exe";

    private readonly IShellOperations shell;
    private readonly AppRuntimeOptions options;
    private readonly IAppLog log;
    private readonly LegacyRegistryLocations locations;
    private readonly Func<nint> findWindow;

    /// <summary>Creates the service.</summary>
    /// <param name="shell">Resolves and recycles the 5.4 Startup shortcut.</param>
    /// <param name="options">With <see cref="AppRuntimeOptions.AllowSystemChanges"/> false, fixes are skipped and logged.</param>
    /// <param name="log">The log.</param>
    public LegacyLeftovers(IShellOperations shell, AppRuntimeOptions options, IAppLog log)
        : this(shell, options, log, LegacyRegistryLocations.Default, LegacyNativeMethods.FindLegacyWindow)
    {
    }

    /// <summary>Creates the service for other locations and another window finder (tests).</summary>
    /// <param name="shell">Resolves and recycles shortcuts.</param>
    /// <param name="options">The runtime options.</param>
    /// <param name="log">The log.</param>
    /// <param name="locations">The registry key and Startup folder.</param>
    /// <param name="findWindow">Finds the running 5.4 main window (0 when none).</param>
    internal LegacyLeftovers(IShellOperations shell, AppRuntimeOptions options, IAppLog log, LegacyRegistryLocations locations, Func<nint> findWindow)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        this.shell = shell;
        this.options = options;
        this.log = log;
        this.locations = locations;
        this.findWindow = findWindow;
    }

    /// <inheritdoc />
    public LegacyLeftoverState Detect() => new(findWindow() != 0, FindStartupShortcut());

    /// <inheritdoc />
    public bool CloseRunningInstance()
    {
        nint window = findWindow();
        if (window == 0)
        {
            return false;
        }

        if (!options.AllowSystemChanges)
        {
            log.Info(LogSource, "System changes are off: Holiday Lights 5.4 would have been sent its Exit command.");
            return false;
        }

        bool sent = LegacyNativeMethods.SendCommand(window, LegacyNativeMethods.ExitCommand);
        log.Info(LogSource, sent ? "Sent Holiday Lights 5.4 its Exit command." : "Holiday Lights 5.4 did not take its Exit command.");
        return sent;
    }

    /// <inheritdoc />
    public bool RemoveStartupShortcut()
    {
        if (FindStartupShortcut() is not { } shortcut)
        {
            return false;
        }

        if (!options.AllowSystemChanges)
        {
            log.Info(LogSource, "System changes are off: the Holiday Lights 5.4 Startup shortcut would have been recycled.");
            return false;
        }

        bool recycled = shell.MoveToRecycleBin(shortcut);
        log.Info(LogSource, recycled ? "Moved the Holiday Lights 5.4 Startup shortcut to the Recycle Bin." : "The Holiday Lights 5.4 Startup shortcut could not be recycled.");
        return recycled;
    }

    private string? FindStartupShortcut()
    {
        try
        {
            HashSet<string> programs = ProgramPaths();
            return LegacyStartupShortcuts.Find(locations.Startup, shell)
                .FirstOrDefault(s => s.Value is { } target && IsLegacyProgram(target, programs)).Key;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            log.Warn(LogSource, "The Startup folder could not be searched for Holiday Lights 5.4.", e);
            return null;
        }
    }

    private static bool IsLegacyProgram(string target, HashSet<string> programs)
    {
        string longTarget = LegacyNativeMethods.GetLongPath(target) ?? target;
        return string.Equals(Path.GetFileName(longTarget), ProgramFileName, StringComparison.OrdinalIgnoreCase) || programs.Contains(longTarget);
    }

    /// <summary>The 5.4 program paths of the HKCU and HKLM <c>Path</c> values, in their long forms.</summary>
    private HashSet<string> ProgramPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (RegistryKey? user = Registry.CurrentUser.OpenSubKey(locations.UserKeyPath, writable: false))
        {
            Add(user?.GetValue("Path") as string);
        }

        if (locations.MachineKeyPath is not null)
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using RegistryKey? key = machine.OpenSubKey(locations.MachineKeyPath, writable: false);
            Add(key?.GetValue("Path") as string);
        }

        return paths;

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(LegacyNativeMethods.GetLongPath(path.Trim().Trim('"')) ?? path);
            }
        }
    }
}
