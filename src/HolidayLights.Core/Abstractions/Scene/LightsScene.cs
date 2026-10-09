namespace HolidayLights.Core.Abstractions;

/// <summary>Where the lights are drawn on a display (PRODUCT-SPEC 5.1, layer modes (a), (b), (c)).</summary>
public enum LayerMode
{
    /// <summary>(a) Behind the desktop icons: a DirectComposition child of the desktop layer parent below <c>SHELLDLL_DefView</c>.</summary>
    BehindIcons,

    /// <summary>(b) In front of the icons, behind every window: a top-level layer owned by Progman at <c>HWND_BOTTOM</c>.</summary>
    InFrontOfIcons,

    /// <summary>(c) On top of all windows, directly below the taskbars.</summary>
    OnTop,
}

/// <summary>Maps the persisted "Bulb Drawing" settings to a layer mode.</summary>
public static class LayerModes
{
    /// <summary>On Desktop + Behind the Desktop Icons = (a); On Desktop alone = (b); On Top = (c).</summary>
    /// <param name="drawing">"Bulb Drawing".</param>
    /// <param name="behindIcons">"Behind the Desktop Icons".</param>
    /// <returns>The requested layer mode.</returns>
    public static LayerMode FromSettings(BulbDrawing drawing, bool behindIcons) =>
        drawing == BulbDrawing.OnTop ? LayerMode.OnTop : behindIcons ? LayerMode.BehindIcons : LayerMode.InFrontOfIcons;
}

/// <summary>How the lights look (the effective Look; PRODUCT-SPEC 4.5, 5.8, 5.9, 5.12.3).</summary>
public sealed record SceneEffects
{
    /// <summary>Pixel-art scaling style.</summary>
    public SpriteStyle Pixels { get; init; } = SpriteStyle.Smooth;

    /// <summary>Glow level (Off under "Use Less Power", "Stop Flashing" and Classic 2003).</summary>
    public GlowLevel Glow { get; init; } = GlowLevel.Soft;

    /// <summary>Windows "Animation effects" are off: power-up, transitions and layer moves become plain fades or instant (4.4). The lights keep flashing.</summary>
    public bool ReducedMotion { get; init; }
}

/// <summary>One display in a scene.</summary>
/// <param name="Display">The display.</param>
/// <param name="Enabled">The user's per-display "Show Lights" choice (General > Displays).</param>
public sealed record DisplayScene(DisplayInfo Display, bool Enabled);

/// <summary>
/// An immutable description of everything the lights should show, built on the UI thread (app-shell's scene builder)
/// and handed to the presenter (rendering) with <c>LightsPresenter.Apply</c>. Previews read the same scene so they match
/// the desktop exactly.
/// </summary>
public sealed record LightsScene
{
    /// <summary>An empty scene (no displays, lights off).</summary>
    public static LightsScene Empty { get; } = new() { LightsOn = false, Displays = [], Layout = LightsLayout.Empty };

    /// <summary>Increases with every scene the builder produces (diagnostics and change detection).</summary>
    public long Revision { get; init; }

    /// <summary>"Show Lights" (and not turned off by the "Turn Off the Lights" energy-saver rule).</summary>
    public required bool LightsOn { get; init; }

    /// <summary>The requested layer mode; the presenter falls back (a) to (b) to (c) when needed and reports it in <see cref="LightsStatus"/>.</summary>
    public LayerMode RequestedLayer { get; init; } = LayerMode.BehindIcons;

    /// <summary>Every connected display with its enabled flag.</summary>
    public required IReadOnlyList<DisplayScene> Displays { get; init; }

    /// <summary>The layout of the enabled displays (work areas, scale S per display, the frame mode).</summary>
    public required LightsLayout Layout { get; init; }

    /// <summary>The effective flash options (energy-saver and limit rules applied; seeds chosen when the pattern or layout changed).</summary>
    public FlashOptions Flash { get; init; } = new();

    /// <summary>The effective flash interval (1-9; at least 3 with Limit Flashing, at least 5 under "Use Less Power").</summary>
    public int Interval { get; init; } = FlashSettings.DefaultInterval;

    /// <summary>The effective look.</summary>
    public SceneEffects Effects { get; init; } = new();

    /// <summary>
    /// The "When Energy Saver Is On" rule that shaped this scene while Energy Saver is on (Home status line "Using less
    /// power while Energy Saver is on."), or null when Energy Saver is off or the choice is "Change Nothing".
    /// </summary>
    public EnergySaverChoice? EnergySaverInEffect { get; init; }
}

/// <summary>How the presenter moves from the previous scene to a new one (PRODUCT-SPEC 4.3.2, 4.3.3, 4.4).</summary>
public enum SceneTransition
{
    /// <summary>
    /// Edits: only affected displays re-lay out; new bulbs fade in 150 ms, removed ones fade out 150 ms; a layer move fades
    /// out 150 ms, moves and fades in 150 ms; lights off fade out 300 ms. Instant under reduced motion.
    /// </summary>
    Automatic,

    /// <summary>No animation at all.</summary>
    None,

    /// <summary>First launch after install: dark bulbs fade in (200 ms), a wave runs clockwise (1600 ms ring), everything lit for 300 ms, then the pattern starts at step 0 (2200 ms).</summary>
    FirstRunPowerUp,

    /// <summary>Sign-in autostart: the short power-up with a 1.0 s ring, no hold.</summary>
    AutostartPowerUp,

    /// <summary>"Show Lights" turned on: the short power-up with a 0.8 s ring.</summary>
    ShortPowerUp,

    /// <summary>A theme was loaded (by hand or automatically): old lights fade out over 250 ms, then the short power-up (0.8 s).</summary>
    ThemeTransition,
}

/// <summary>Reasons for the lights to rest on every display (PRODUCT-SPEC 5.12.2, 5.12.3). Several can apply.</summary>
[Flags]
public enum PauseReasons
{
    /// <summary>Nothing pauses the lights.</summary>
    None = 0,

    /// <summary>The session is locked: hidden, clock stopped.</summary>
    SessionLocked = 1,

    /// <summary>The display is off (<c>GUID_SESSION_DISPLAY_STATUS</c> = 0): clock stopped, no commits.</summary>
    DisplayOff = 2,

    /// <summary>Another screen saver runs or the session is not present (<c>QUNS_NOT_PRESENT</c>).</summary>
    UserNotPresent = 4,

    /// <summary>The Holiday Lights screen saver (or "Preview Screen Saver") is running: layers stop committing.</summary>
    OwnScreenSaver = 8,

    /// <summary>Presentation mode (<c>QUNS_PRESENTATION_MODE</c>) and "Hide the Lights During Presentations" is on.</summary>
    Presentation = 16,

    /// <summary>Exclusive full-screen Direct3D or Game Mode, and the full-screen rule is on.</summary>
    ExclusiveFullScreen = 32,

    /// <summary>A Remote Desktop session.</summary>
    RemoteSession = 64,
}

/// <summary>What makes the lights rest right now; the presenter hides layers and stops its clock accordingly.</summary>
/// <param name="Global">Reasons that apply to every display.</param>
/// <param name="RestingDisplayIds">Displays with a full-screen app in front (hidden on those displays only).</param>
public sealed record LightsPauseState(PauseReasons Global, IReadOnlySet<string> RestingDisplayIds)
{
    /// <summary>Nothing rests.</summary>
    public static LightsPauseState None { get; } = new(PauseReasons.None, new HashSet<string>());

    /// <summary>True when a global reason applies.</summary>
    public bool IsGloballyPaused => Global != PauseReasons.None;
}

/// <summary>Overall health of the presenter.</summary>
public enum LightsHealth
{
    /// <summary>Creating the device and layers.</summary>
    Starting,

    /// <summary>Drawing normally.</summary>
    Running,

    /// <summary>No DirectComposition device can be created; retried every 10 s (Home status line).</summary>
    NoGraphicsDevice,

    /// <summary>Drawing with WARP after two device losses within a minute (Bulb Factory InfoBar).</summary>
    SoftwareRendering,

    /// <summary>Shut down.</summary>
    Stopped,
}

/// <summary>The effective state of one display's layer.</summary>
/// <param name="DisplayId">The display.</param>
/// <param name="Effective">The layer mode in use, or null when nothing is shown there (disabled, resting, lights off, no device).</param>
/// <param name="Resting">True when a pause reason hides this display.</param>
public sealed record DisplayLayerStatus(string DisplayId, LayerMode? Effective, bool Resting);

/// <summary>What the presenter is doing (tray tooltip, Home status line, Bulb Drawing status, Copy Version Info).</summary>
public sealed record LightsStatus
{
    /// <summary>The status before the presenter started.</summary>
    public static LightsStatus Initial { get; } = new() { Health = LightsHealth.Starting, Displays = [] };

    /// <summary>Health.</summary>
    public required LightsHealth Health { get; init; }

    /// <summary>The requested layer mode of the current scene.</summary>
    public LayerMode Requested { get; init; } = LayerMode.BehindIcons;

    /// <summary>One entry per enabled display.</summary>
    public required IReadOnlyList<DisplayLayerStatus> Displays { get; init; }

    /// <summary>The global pause reasons in effect.</summary>
    public PauseReasons Paused { get; init; }

    /// <summary>How often the Lights thread was restarted after a crash this session ("Lights restarted" notification once).</summary>
    public int RestartCount { get; init; }

    /// <summary>Timestamp of the first committed frame (goal G1), or null before it.</summary>
    public long? FirstFrameTimestamp { get; init; }

    /// <summary>True when a visible display uses a fallback mode instead of the requested one (status line, "Try Again").</summary>
    public bool IsFallbackActive => Displays.Any(d => d.Effective is { } mode && mode != Requested);
}

/// <summary>Arguments of the presenter's status event.</summary>
public sealed class LightsStatusChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="status">The new status.</param>
    public LightsStatusChangedEventArgs(LightsStatus status) => Status = status;

    /// <summary>The new status.</summary>
    public LightsStatus Status { get; }
}

/// <summary>The icon of the on-screen pill (PRODUCT-SPEC 3.12).</summary>
public enum PillGlyph
{
    /// <summary>"Bulbs on top of all windows".</summary>
    OnTop,

    /// <summary>"Bulbs on the desktop".</summary>
    OnDesktop,

    /// <summary>"Lights on".</summary>
    LightsOn,

    /// <summary>"Lights off".</summary>
    LightsOff,
}

/// <summary>A request to show the on-screen pill (hot key feedback) on the display under the mouse pointer.</summary>
/// <param name="Glyph">The icon.</param>
/// <param name="Text">White text, e.g. "Bulbs on top of all windows".</param>
/// <param name="SecondLine">Optional hint shown for <paramref name="SecondLineDuration"/> ("Press Ctrl+Alt+Shift+B again to put them back.").</param>
/// <param name="SecondLineDuration">How long the second line stays (4 s in the spec).</param>
public sealed record PillRequest(PillGlyph Glyph, string Text, string? SecondLine = null, TimeSpan SecondLineDuration = default);
