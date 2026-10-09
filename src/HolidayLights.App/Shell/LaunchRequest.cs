namespace HolidayLights.App.Shell;

/// <summary>What a launch asks for (PRODUCT-SPEC 6.6.3 and 6.2.1).</summary>
public enum LaunchKind
{
    /// <summary>No command: start (lights and tray, no window), or open Settings when already running.</summary>
    Normal,

    /// <summary><c>--autostart</c>: the Run value; no window, the short power-up.</summary>
    Autostart,

    /// <summary><c>--settings [page]</c>: open Settings on a page.</summary>
    Settings,

    /// <summary>Legacy <c>settings</c>: Screen Saver page; a settings-only session when not running.</summary>
    LegacySettings,

    /// <summary><c>--open "&lt;file&gt;" ...</c> or legacy <c>open &lt;file&gt;</c>: add files.</summary>
    Open,

    /// <summary><c>--toggle-layer</c>.</summary>
    ToggleLayer,

    /// <summary><c>--lights on|off|toggle</c>.</summary>
    Lights,

    /// <summary><c>--theme "&lt;name&gt;"</c>.</summary>
    Theme,

    /// <summary><c>--exit</c>.</summary>
    Exit,

    /// <summary><c>--reset</c> or legacy <c>reset</c>: confirm, then reset the 6.0 settings.</summary>
    Reset,

    /// <summary><c>--render-test &lt;dir&gt;</c>: one PNG per display of the current scene, no windows.</summary>
    RenderTest,

    /// <summary><c>--install</c>: the per-user installer (6.10).</summary>
    Install,

    /// <summary><c>--uninstall</c>: the per-user uninstaller (6.10).</summary>
    Uninstall,

    /// <summary><c>/s</c>: the full-screen screen saver.</summary>
    ScreenSaverShow,

    /// <summary><c>/p &lt;hwnd&gt;</c> or <c>/p:&lt;hwnd&gt;</c>: the preview inside Windows' dialog.</summary>
    ScreenSaverPreview,

    /// <summary><c>/c</c>, <c>/c:&lt;hwnd&gt;</c> or a <c>.scr</c> launch without arguments: Settings on Screen Saver.</summary>
    ScreenSaverConfigure,

    /// <summary><c>/a</c>: ignored (Win9x passwords).</summary>
    ScreenSaverPassword,

    /// <summary>
    /// <c>--diagnostics [&lt;file&gt;]</c>: writes the "Copy Version Info" report and an installation check to the file
    /// (or to standard output) without windows, then exits.
    /// </summary>
    Diagnostics,
}

/// <summary>A parsed command line.</summary>
public sealed record LaunchRequest
{
    /// <summary>What is asked.</summary>
    public LaunchKind Kind { get; init; } = LaunchKind.Normal;

    /// <summary>The page of <see cref="LaunchKind.Settings"/>.</summary>
    public SettingsPageId? Page { get; init; }

    /// <summary>Files of <see cref="LaunchKind.Open"/>.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>
    /// "on", "off" or "toggle" (<see cref="LaunchKind.Lights"/>), the theme name, the render-test folder or the
    /// diagnostics file.
    /// </summary>
    public string? Argument { get; init; }

    /// <summary>The window handle of <c>/p</c> and <c>/c:</c> (0 when none).</summary>
    public nint WindowHandle { get; init; }

    /// <summary><c>--data-root &lt;dir&gt;</c>, or null.</summary>
    public string? DataRoot { get; init; }

    /// <summary><c>--no-system-changes</c>.</summary>
    public bool NoSystemChanges { get; init; }

    /// <summary>Arguments that were not understood (ignored; the caller logs them).</summary>
    public IReadOnlyList<string> UnknownArguments { get; init; } = [];

    /// <summary>True for the screen saver modes, which never take the single-instance mutex.</summary>
    public bool IsScreenSaverMode => Kind is LaunchKind.ScreenSaverShow or LaunchKind.ScreenSaverPreview
        or LaunchKind.ScreenSaverConfigure or LaunchKind.ScreenSaverPassword;
}
