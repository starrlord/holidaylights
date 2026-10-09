namespace HolidayLights.Core.Abstractions;

/// <summary>What kind of process this is.</summary>
public enum AppSessionKind
{
    /// <summary>The normal app: lights, tray, hot keys, music.</summary>
    Normal,

    /// <summary>Settings only (screen saver "Settings" button or legacy <c>settings</c> without a running app): no lights, tray, hot keys or music; exits when Settings closes.</summary>
    SettingsOnly,

    /// <summary>The full-screen screen saver (<c>/s</c>).</summary>
    ScreenSaver,

    /// <summary>The screen saver preview inside Windows' dialog (<c>/p</c>).</summary>
    ScreenSaverPreview,

    /// <summary><c>--render-test &lt;dir&gt;</c>: writes PNGs without creating windows.</summary>
    RenderTest,

    /// <summary>The per-user installer or uninstaller.</summary>
    Setup,
}

/// <summary>Process-wide options decided by the command line (PRODUCT-SPEC 6.6.3, PO-3).</summary>
public sealed record AppRuntimeOptions
{
    /// <summary>Environment variable equivalent of <c>--no-system-changes</c> ("1" or "true").</summary>
    public const string NoSystemChangesVariable = "HOLIDAYLIGHTS_NO_SYSTEM_CHANGES";

    /// <summary>
    /// False with <c>--no-system-changes</c>: the Run key, the file association, the Windows screen saver registration,
    /// shortcuts and the 5.4 leftovers are never touched (the services log what they would have done).
    /// </summary>
    public bool AllowSystemChanges { get; init; } = true;

    /// <summary>What kind of process this is.</summary>
    public AppSessionKind Session { get; init; } = AppSessionKind.Normal;

    /// <summary>Started by the Run value (<c>--autostart</c>): no Welcome card, no Settings, the short power-up.</summary>
    public bool StartedAtSignIn { get; init; }

    /// <summary>Reads <see cref="NoSystemChangesVariable"/>.</summary>
    /// <returns>Options for a normal session.</returns>
    public static AppRuntimeOptions FromEnvironment() =>
        new() { AllowSystemChanges = !IsSet(Environment.GetEnvironmentVariable(NoSystemChangesVariable)) };

    private static bool IsSet(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
