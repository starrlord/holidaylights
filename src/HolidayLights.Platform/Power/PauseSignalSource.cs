using HolidayLights.Platform.Native;
using Microsoft.Win32;
using Windows.UI.Shell;

namespace HolidayLights.Platform.Power;

/// <summary>
/// Power, session, full-screen, presentation, Game Mode, Focus session and Remote Desktop signals
/// (see <see cref="IPauseSignalSource"/>). Owner: platform.
/// </summary>
/// <remarks>
/// <para>Event-driven, without windows: display state, Energy Saver and battery saver through power setting callbacks,
/// Game Mode through the effective power mode, lock and unlock through <see cref="SystemEvents.SessionSwitch"/> (the
/// state at start from <c>WTSQuerySessionInformation</c>). Polled every second on the thread pool:
/// <c>SHQueryUserNotificationState</c>, the full-screen app per display (<see cref="FullScreenDetector"/>), the Focus
/// session and the Remote Desktop state. While the lights rest on every display whatever the settings (the session is
/// locked, the displays are off, another screen saver runs, Remote Desktop), the poll slows to every 2 s
/// (PRODUCT-SPEC 5.12.2, 5.14), and it runs at once when that ends.</para>
/// <para><see cref="Current"/> is an immutable snapshot; <see cref="Changed"/> is raised (on the thread that noticed it)
/// only when a signal really changed.</para>
/// </remarks>
public sealed class PauseSignalSource : IPauseSignalSource
{
    private const string LogSource = "Platform.Pause";
    private const int DisplayOff = 0;
    private const int GameModeValue = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RestingPollInterval = TimeSpan.FromSeconds(2);

    private readonly IDisplayService displays;
    private readonly IAppLog log;
    private readonly Lock gate = new();
    private readonly FullScreenDetector fullScreenDetector = new();
    private PauseSignals current = PauseSignals.None;
    private PolledSignals polled = PolledSignals.Initial;
    private bool sessionLocked;
    private int displayStatus = 1;
    private int energySaverStatus;
    private int powerSavingStatus;
    private int effectivePowerMode = -1;
    private PowerNotifications? power;
    private volatile FocusSessionManager? focusSessions;
    private Timer? timer;
    private TimeSpan pollPeriod;
    private int polling;
    private bool started;
    private bool disposed;

    /// <summary>Creates the source; call <see cref="Start"/>.</summary>
    /// <param name="displays">Maps the full-screen foreground window's monitor to a display id.</param>
    /// <param name="log">The log.</param>
    public PauseSignalSource(IDisplayService displays, IAppLog log)
    {
        this.displays = displays;
        this.log = log;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public PauseSignals Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed)
            {
                return;
            }

            started = true;
            sessionLocked = SessionState.IsLocked();
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        focusSessions = OpenFocusSessions();
        power = new PowerNotifications(
            [PowerNotifications.SessionDisplayStatus, PowerNotifications.EnergySaverStatus, PowerNotifications.PowerSavingStatus],
            OnPowerSetting,
            OnEffectivePowerMode,
            log);
        Poll();
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            pollPeriod = PollPeriodFor(current);
            timer = new Timer(_ => Poll(), null, pollPeriod, pollPeriod);
        }
    }

    /// <summary>Unregisters the callbacks and stops polling.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        timer?.Dispose();
        if (started)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }

        power?.Dispose();
    }

    /// <summary>Signal equality, with the full-screen display sets compared by content.</summary>
    internal static bool SameSignals(PauseSignals a, PauseSignals b) =>
        a.SessionLocked == b.SessionLocked &&
        a.DisplayOff == b.DisplayOff &&
        a.NotificationState == b.NotificationState &&
        a.EnergySaverOn == b.EnergySaverOn &&
        a.GameMode == b.GameMode &&
        a.FocusSessionActive == b.FocusSessionActive &&
        a.RemoteSession == b.RemoteSession &&
        a.FullScreenDisplayIds.SetEquals(b.FullScreenDisplayIds);

    /// <summary>
    /// How often to poll: every 2 s while the lights rest on every display whatever the settings (locked, displays off,
    /// another screen saver, Remote Desktop; music keeps its pause rules with at most 2 s of delay), else every second.
    /// </summary>
    /// <param name="signals">The current signals.</param>
    /// <returns>The poll period.</returns>
    internal static TimeSpan PollPeriodFor(PauseSignals signals) =>
        signals.SessionLocked || signals.DisplayOff || signals.RemoteSession ||
        signals.NotificationState == UserNotificationState.NotPresent
            ? RestingPollInterval
            : PollInterval;

    private static UserNotificationState QueryNotificationState() =>
        NativeMethods.SHQueryUserNotificationState(out int state) == 0 && Enum.IsDefined((UserNotificationState)state)
            ? (UserNotificationState)state
            : UserNotificationState.Unknown;

    private static string Describe(PauseSignals s) =>
        $"locked={s.SessionLocked}, displayOff={s.DisplayOff}, notifications={s.NotificationState}, " +
        $"fullScreenDisplays={s.FullScreenDisplayIds.Count}, energySaver={s.EnergySaverOn}, gameMode={s.GameMode}, " +
        $"focus={s.FocusSessionActive}, remote={s.RemoteSession}";

    private FocusSessionManager? OpenFocusSessions()
    {
        try
        {
            return FocusSessionManager.IsSupported ? FocusSessionManager.GetDefault() : null;
        }
        catch (Exception e)
        {
            // An optional signal: whatever the WinRT layer throws, music simply never pauses for Focus sessions.
            log.Warn(LogSource, "Focus sessions cannot be read on this Windows.", e);
            return null;
        }
    }

    private bool IsFocusActive()
    {
        FocusSessionManager? manager = focusSessions;
        try
        {
            return manager?.IsFocusActive ?? false;
        }
        catch (Exception e)
        {
            // Stop asking instead of failing every second.
            focusSessions = null;
            log.Warn(LogSource, "Focus sessions stopped answering; they are no longer watched.", e);
            return false;
        }
    }

    private void OnPowerSetting(Guid setting, int value)
    {
        lock (gate)
        {
            if (setting == PowerNotifications.SessionDisplayStatus)
            {
                displayStatus = value;
            }
            else if (setting == PowerNotifications.EnergySaverStatus)
            {
                energySaverStatus = value;
            }
            else if (setting == PowerNotifications.PowerSavingStatus)
            {
                powerSavingStatus = value;
            }
        }

        Publish();
    }

    private void OnEffectivePowerMode(int mode)
    {
        lock (gate)
        {
            effectivePowerMode = mode;
        }

        Publish();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        bool locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock => true,
            SessionSwitchReason.SessionUnlock => false,
            _ => SessionState.IsLocked(),
        };
        lock (gate)
        {
            sessionLocked = locked;
        }

        // Remote connects and disconnects change the Remote Desktop state too: read everything now.
        Poll();
    }

    private void Poll()
    {
        if (Interlocked.Exchange(ref polling, 1) == 1)
        {
            return;
        }

        try
        {
            IReadOnlySet<string> fullScreen = fullScreenDetector.Detect(displays.Displays);
            var next = new PolledSignals(
                QueryNotificationState(),
                fullScreen,
                IsFocusActive(),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0);
            lock (gate)
            {
                polled = next;
            }

            Publish();
        }
        catch (Exception e)
        {
            // A timer callback must never throw.
            log.Error(LogSource, "Reading the pause signals failed.", e);
        }
        finally
        {
            Volatile.Write(ref polling, 0);
        }
    }

    private void Publish()
    {
        PauseSignals next;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            next = new PauseSignals
            {
                SessionLocked = sessionLocked,
                DisplayOff = displayStatus == DisplayOff,
                NotificationState = polled.NotificationState,
                FullScreenDisplayIds = polled.FullScreenDisplayIds,
                EnergySaverOn = energySaverStatus >= 1 || powerSavingStatus == 1,
                GameMode = effectivePowerMode == GameModeValue,
                FocusSessionActive = polled.FocusSessionActive,
                RemoteSession = polled.RemoteSession,
            };
            if (SameSignals(next, current))
            {
                return;
            }

            current = next;
            Reschedule();
        }

        log.Info(LogSource, $"Pause signals: {Describe(next)}.");
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            log.Error(LogSource, "A pause signal subscriber failed.", e);
        }
    }

    /// <summary>Follows <see cref="PollPeriodFor"/> after a change; polls at once when the rest ends. Caller holds the gate.</summary>
    private void Reschedule()
    {
        TimeSpan wanted = PollPeriodFor(current);
        if (timer is null || wanted == pollPeriod)
        {
            return;
        }

        // The timer is disposed only after 'disposed' is set under the gate, so it is alive here.
        TimeSpan due = wanted < pollPeriod ? TimeSpan.Zero : wanted;
        pollPeriod = wanted;
        timer.Change(due, wanted);
    }

    /// <summary>The polled part of the signals.</summary>
    private sealed record PolledSignals(
        UserNotificationState NotificationState, IReadOnlySet<string> FullScreenDisplayIds, bool FocusSessionActive, bool RemoteSession)
    {
        public static PolledSignals Initial { get; } = new(UserNotificationState.AcceptsNotifications, new HashSet<string>(), false, false);
    }
}
