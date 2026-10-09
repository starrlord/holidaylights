using System.Globalization;

namespace HolidayLights.Platform.Integration;

/// <summary>Reads and changes the Windows screen saver (see <see cref="IScreenSaverRegistration"/>). Owner: platform.</summary>
/// <remarks>
/// <para>Windows keeps the saver in <c>HKCU\Control Panel\Desktop</c>: <c>SCRNSAVE.EXE</c> (full path; there is no
/// <c>SystemParametersInfo</c> action for it, so it is written to the registry), <c>ScreenSaveActive</c> and
/// <c>ScreenSaveTimeOut</c> (written with <c>SystemParametersInfo(SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)</c>, whose
/// broadcast also makes Windows pick up the new path). Values under
/// <c>HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop</c> override them and are reported as managed.</para>
/// <para>Holiday Lights becomes the saver only on request (PRODUCT-SPEC 6.2.4): <see cref="Use"/> installs the
/// <c>Holiday Lights.scr</c> copy of the program when it is missing or outdated, points <c>SCRNSAVE.EXE</c> at its full
/// long path (the 5.4 short <c>system32</c> path is what broke 5.4 on 64-bit Windows) and returns the previous values;
/// <see cref="StopUsing"/> restores them only while Holiday Lights is still the saver.</para>
/// </remarks>
public sealed class ScreenSaverRegistration : IScreenSaverRegistration
{
    /// <summary>The user's desktop settings (relative to HKCU).</summary>
    internal const string DesktopKey = @"Control Panel\Desktop";

    /// <summary>The policy override of the same values (relative to HKCU).</summary>
    internal const string PolicyKey = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";

    /// <summary>The saver path value.</summary>
    internal const string SaverValue = "SCRNSAVE.EXE";

    /// <summary>"1" when the screen saver is on.</summary>
    internal const string ActiveValue = "ScreenSaveActive";

    /// <summary>Idle seconds before the saver starts.</summary>
    internal const string TimeoutValue = "ScreenSaveTimeOut";

    /// <summary>The timeout used when Windows has none (10 minutes).</summary>
    internal const int DefaultTimeoutSeconds = 600;

    private const string LogSource = "Platform.ScreenSaver";
    private static readonly string[] PolicyValues = [SaverValue, ActiveValue, TimeoutValue, "ScreenSaverIsSecure"];

    private readonly DataPaths paths;
    private readonly IShellOperations shell;
    private readonly IAppLog log;
    private readonly UserRegistry registry;
    private readonly IDesktopParameters parameters;
    private readonly SystemChanges changes;

    /// <summary>Creates the service.</summary>
    /// <param name="paths">Our installed <c>.scr</c> (<see cref="DataPaths.InstalledScreenSaverPath"/>).</param>
    /// <param name="shell">Opens the Windows screen saver dialog.</param>
    /// <param name="options">With <see cref="AppRuntimeOptions.AllowSystemChanges"/> false, writes are skipped and logged.</param>
    /// <param name="log">The log.</param>
    public ScreenSaverRegistration(DataPaths paths, IShellOperations shell, AppRuntimeOptions options, IAppLog log)
        : this(paths, shell, options, log, UserRegistry.CurrentUser, SystemDesktopParameters.Instance)
    {
    }

    /// <summary>Creates the service on another registry root and parameter writer (tests).</summary>
    /// <param name="paths">Our installed <c>.scr</c>.</param>
    /// <param name="shell">Opens the Windows screen saver dialog.</param>
    /// <param name="options">Whether writes are allowed.</param>
    /// <param name="log">The log.</param>
    /// <param name="registry">The key that stands for HKCU.</param>
    /// <param name="parameters">Writes ScreenSaveActive and ScreenSaveTimeOut.</param>
    internal ScreenSaverRegistration(
        DataPaths paths, IShellOperations shell, AppRuntimeOptions options, IAppLog log, UserRegistry registry, IDesktopParameters parameters)
    {
        this.paths = paths;
        this.shell = shell;
        this.log = log;
        this.registry = registry;
        this.parameters = parameters;
        changes = new SystemChanges(options, log, LogSource);
    }

    /// <inheritdoc />
    public ScreenSaverStatus GetStatus()
    {
        bool policyManaged = IsPolicyManaged();
        string? saver = NullIfBlank(registry.ReadString(PolicyKey, SaverValue) ?? registry.ReadString(DesktopKey, SaverValue));
        bool active = IsActive(registry.ReadString(PolicyKey, ActiveValue) ?? registry.ReadString(DesktopKey, ActiveValue));
        int timeout = ParseTimeout(registry.ReadString(PolicyKey, TimeoutValue) ?? registry.ReadString(DesktopKey, TimeoutValue))
                      ?? DefaultTimeoutSeconds;
        return new ScreenSaverStatus(Classify(saver, active), saver, timeout, policyManaged);
    }

    /// <inheritdoc />
    public ScreenSaverPrevious Use()
    {
        string? rawTimeout = registry.ReadString(DesktopKey, TimeoutValue);
        var previous = new ScreenSaverPrevious
        {
            ScrnsaveExe = NullIfBlank(registry.ReadString(DesktopKey, SaverValue)),
            Active = IsActive(registry.ReadString(DesktopKey, ActiveValue)),
            TimeoutSeconds = ParseTimeout(rawTimeout) ?? DefaultTimeoutSeconds,
        };

        if (IsPolicyManaged())
        {
            log.Warn(LogSource, "Your organization manages the screen saver; Windows may keep its own settings.");
        }

        changes.Apply("make Holiday Lights the screen saver", () =>
        {
            InstallScreenSaverFile();
            registry.Write(DesktopKey, SaverValue, ScreenSaverPath());
            if (ParseTimeout(rawTimeout) is null)
            {
                parameters.SetScreenSaveTimeout(DefaultTimeoutSeconds);
            }

            // Last: its WM_SETTINGCHANGE broadcast makes Windows read the new path too.
            parameters.SetScreenSaveActive(true);
        });
        return previous;
    }

    /// <inheritdoc />
    public void StopUsing(ScreenSaverPrevious? previous)
    {
        ScreenSaverStatus status = GetStatus();
        if (status.State is not (ScreenSaverState.Ours or ScreenSaverState.OursButTurnedOff))
        {
            log.Info(LogSource, "Holiday Lights is not the screen saver; nothing to restore.");
            return;
        }

        // A remembered 5.4 path (broken on this Windows) or our own path is not restored: "(None)" is used.
        string? restore = NullIfBlank(previous?.ScrnsaveExe);
        if (restore is not null && Classify(restore, active: true) is ScreenSaverState.Legacy54 or ScreenSaverState.Ours)
        {
            restore = null;
        }

        changes.Apply("restore the previous screen saver", () =>
        {
            if (restore is null)
            {
                registry.DeleteValue(DesktopKey, SaverValue);
            }
            else
            {
                registry.Write(DesktopKey, SaverValue, restore);
            }

            if (previous is not null && previous.TimeoutSeconds > 0)
            {
                parameters.SetScreenSaveTimeout(previous.TimeoutSeconds);
            }

            parameters.SetScreenSaveActive(previous?.Active ?? status.State == ScreenSaverState.Ours);
        });
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The timeout is shorter than one second.</exception>
    public void SetTimeout(TimeSpan timeout)
    {
        int seconds = (int)Math.Round(timeout.TotalSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1, nameof(timeout));
        changes.Apply($"set the screen saver timeout to {seconds} s", () => parameters.SetScreenSaveTimeout(seconds));
    }

    /// <inheritdoc />
    public void TurnOn() => changes.Apply("turn the screen saver on", () => parameters.SetScreenSaveActive(true));

    /// <inheritdoc />
    public void OpenWindowsSettings() => shell.OpenControlPanel("desk.cpl,,@screensaver");

    /// <summary>
    /// The per-user install of the screen saver: copies <c>HolidayLights.exe</c> to <c>Holiday Lights.scr</c> in the
    /// install folder when the copy is missing or differs (size or time), through a temporary file. Called by
    /// <see cref="Use"/>; the installer may call it too.
    /// </summary>
    /// <returns>True when the <c>.scr</c> exists afterwards.</returns>
    public bool InstallScreenSaverFile()
    {
        string program = paths.InstalledExePath;
        string saver = paths.InstalledScreenSaverPath;
        if (!File.Exists(program) || IsCurrentCopy(program, saver))
        {
            return File.Exists(saver);
        }

        changes.Apply("install Holiday Lights.scr", () =>
        {
            string temporary = saver + ".new";
            File.Copy(program, temporary, overwrite: true);
            File.Move(temporary, saver, overwrite: true);
        });
        return File.Exists(saver);
    }

    private static bool IsCurrentCopy(string program, string saver)
    {
        var source = new FileInfo(program);
        var copy = new FileInfo(saver);
        return copy.Exists && copy.Length == source.Length && copy.LastWriteTimeUtc == source.LastWriteTimeUtc;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>"0" turns the saver off; anything else (and a missing value, the Windows default) means on.</summary>
    private static bool IsActive(string? value) => !string.Equals(value?.Trim(), "0", StringComparison.Ordinal);

    private static int? ParseTimeout(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds > 0 ? seconds : null;

    private bool IsPolicyManaged() => PolicyValues.Any(name => registry.Read(PolicyKey, name) is not null);

    private ScreenSaverState Classify(string? saver, bool active)
    {
        if (saver is null)
        {
            return ScreenSaverState.NotOurs;
        }

        if (PathText.SameFile(saver, paths.InstalledScreenSaverPath))
        {
            return active ? ScreenSaverState.Ours : ScreenSaverState.OursButTurnedOff;
        }

        return LegacyScreenSaver.IsHolidayLights54(saver) ? ScreenSaverState.Legacy54 : ScreenSaverState.NotOurs;
    }

    /// <summary>The full long path written to <c>SCRNSAVE.EXE</c>.</summary>
    private string ScreenSaverPath()
    {
        string full = Path.GetFullPath(paths.InstalledScreenSaverPath);
        return File.Exists(full) ? PathText.LongName(full) : full;
    }
}
