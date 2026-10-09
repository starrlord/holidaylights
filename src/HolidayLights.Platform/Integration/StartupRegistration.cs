namespace HolidayLights.Platform.Integration;

/// <summary>The per-user Run value (see <see cref="IStartupRegistration"/>). Owner: platform.</summary>
/// <remarks>
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Holiday Lights</c> = <c>"&lt;install&gt;\HolidayLights.exe" --autostart</c>
/// (PRODUCT-SPEC 6.6.1). The user's on/off switch in Windows Settings lives in <c>...\Explorer\StartupApproved\Run</c>
/// (first byte odd = turned off); it is read for the InfoBar and never written.
/// </remarks>
public sealed class StartupRegistration : IStartupRegistration
{
    /// <summary>The Run key (relative to HKCU).</summary>
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where Windows Settings keeps the user's startup switch (relative to HKCU).</summary>
    internal const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>The Run value name.</summary>
    internal const string ValueName = "Holiday Lights";

    private const string LogSource = "Platform.Startup";

    private readonly DataPaths paths;
    private readonly UserRegistry registry;
    private readonly SystemChanges changes;

    /// <summary>Creates the service.</summary>
    /// <param name="paths">The installed program path (<see cref="DataPaths.InstalledExePath"/>).</param>
    /// <param name="options">With <see cref="AppRuntimeOptions.AllowSystemChanges"/> false, writes are skipped and logged.</param>
    /// <param name="log">The log.</param>
    public StartupRegistration(DataPaths paths, AppRuntimeOptions options, IAppLog log)
        : this(paths, options, log, UserRegistry.CurrentUser)
    {
    }

    /// <summary>Creates the service on another registry root (tests).</summary>
    /// <param name="paths">The installed program path.</param>
    /// <param name="options">Whether writes are allowed.</param>
    /// <param name="log">The log.</param>
    /// <param name="registry">The key that stands for HKCU.</param>
    internal StartupRegistration(DataPaths paths, AppRuntimeOptions options, IAppLog log, UserRegistry registry)
    {
        this.paths = paths;
        this.registry = registry;
        changes = new SystemChanges(options, log, LogSource);
    }

    /// <inheritdoc />
    public bool IsEnabled => PathText.SameFile(PathText.ProgramOf(registry.ReadString(RunKey, ValueName)), paths.InstalledExePath);

    /// <inheritdoc />
    public bool IsDisabledByWindows => registry.Read(ApprovedKey, ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) != 0;

    /// <summary>The Run value this installation writes.</summary>
    internal string Command => $"\"{paths.InstalledExePath}\" --autostart";

    /// <inheritdoc />
    public void Enable() => changes.Apply("write the Run value", () => registry.Write(RunKey, ValueName, Command));

    /// <inheritdoc />
    public void Disable() => changes.Apply("remove the Run value", () => registry.DeleteValue(RunKey, ValueName));
}
