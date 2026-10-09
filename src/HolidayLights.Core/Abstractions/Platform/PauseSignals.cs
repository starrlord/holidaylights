namespace HolidayLights.Core.Abstractions;

/// <summary><c>SHQueryUserNotificationState</c> results.</summary>
public enum UserNotificationState
{
    /// <summary>Unknown (the query failed).</summary>
    Unknown = 0,

    /// <summary><c>QUNS_NOT_PRESENT</c>: a screen saver runs, the session is locked or inactive.</summary>
    NotPresent = 1,

    /// <summary><c>QUNS_BUSY</c>: a full-screen app or Presentation Settings.</summary>
    Busy = 2,

    /// <summary><c>QUNS_RUNNING_D3D_FULL_SCREEN</c>.</summary>
    RunningD3DFullScreen = 3,

    /// <summary><c>QUNS_PRESENTATION_MODE</c>.</summary>
    PresentationMode = 4,

    /// <summary><c>QUNS_ACCEPTS_NOTIFICATIONS</c>.</summary>
    AcceptsNotifications = 5,

    /// <summary><c>QUNS_QUIET_TIME</c>.</summary>
    QuietTime = 6,

    /// <summary><c>QUNS_APP</c>.</summary>
    App = 7,
}

/// <summary>Raw OS signals that make lights or music rest (PRODUCT-SPEC 5.12).</summary>
public sealed record PauseSignals
{
    /// <summary>No signal set.</summary>
    public static PauseSignals None { get; } = new();

    /// <summary>The session is locked (<c>SessionSwitch</c> + initial <c>WTSSessionInfoEx</c> state).</summary>
    public bool SessionLocked { get; init; }

    /// <summary>The display is off (<c>GUID_SESSION_DISPLAY_STATUS</c> = 0).</summary>
    public bool DisplayOff { get; init; }

    /// <summary>The user notification state (polled every second).</summary>
    public UserNotificationState NotificationState { get; init; } = UserNotificationState.AcceptsNotifications;

    /// <summary>
    /// Displays whose whole area a full-screen app covers: the foreground window, or a window that covered the display while
    /// it was the foreground window and still does, until the user activates another window mostly on that display
    /// (Progman, WorkerW, taskbars, Task View/Alt+Tab, cloaked, minimized and this process's windows skipped); polled every
    /// second, every 2 s while the lights rest on every display.
    /// </summary>
    public IReadOnlySet<string> FullScreenDisplayIds { get; init; } = new HashSet<string>();

    /// <summary>Energy Saver is on (<c>GUID_ENERGY_SAVER_STATUS</c> &gt;= 1, or <c>GUID_POWER_SAVING_STATUS</c> = 1 on older Windows).</summary>
    public bool EnergySaverOn { get; init; }

    /// <summary>The effective power mode is Game Mode (5).</summary>
    public bool GameMode { get; init; }

    /// <summary>A Focus session runs (<c>FocusSessionManager.IsFocusActive</c>).</summary>
    public bool FocusSessionActive { get; init; }

    /// <summary>The session is a Remote Desktop session.</summary>
    public bool RemoteSession { get; init; }
}

/// <summary>
/// Collects pause signals without hidden windows where possible (power callbacks, session events, a 1 s poll, every 2 s
/// while the lights rest on every display, of the full-screen app per display and <c>SHQueryUserNotificationState</c>).
/// Implemented by platform.
/// </summary>
/// <remarks>The rules that turn signals into lights and music pauses (and their settings) live in app-shell's aggregation.</remarks>
public interface IPauseSignalSource : IDisposable
{
    /// <summary>The latest signals.</summary>
    PauseSignals Current { get; }

    /// <summary>Raised on an arbitrary thread when a signal changed; marshal to the UI thread.</summary>
    event EventHandler? Changed;

    /// <summary>Starts listening and polling.</summary>
    void Start();
}
