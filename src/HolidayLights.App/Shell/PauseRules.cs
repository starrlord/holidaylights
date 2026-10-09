namespace HolidayLights.App.Shell;

/// <summary>What rests right now (PRODUCT-SPEC 5.12.2), derived from the OS signals and the "When the Lights Rest" settings.</summary>
/// <param name="Lights">What the presenter hides.</param>
/// <param name="MusicPausedByRules">A pause rule applies to music (full screen, presentation, Focus session, lock).</param>
/// <param name="MusicStopped">A Remote Desktop session: music stops.</param>
/// <param name="FullScreenActive">A full-screen app, game or presentation is in front (no notifications then).</param>
/// <param name="FocusSessionActive">A Focus session runs (no notifications then).</param>
public sealed record PauseState(LightsPauseState Lights, bool MusicPausedByRules, bool MusicStopped, bool FullScreenActive, bool FocusSessionActive)
{
    /// <summary>Nothing rests.</summary>
    public static PauseState None { get; } = new(LightsPauseState.None, false, false, false, false);

    /// <summary>True when notifications must wait (never during a full-screen app or a Focus session, 3.12).</summary>
    public bool SuppressesNotifications => FullScreenActive || FocusSessionActive;
}

/// <summary>The pause aggregation of PRODUCT-SPEC 5.12.2 (the table "Automatic pausing"). Pure.</summary>
public static class PauseRules
{
    /// <summary>Turns the OS signals into what rests.</summary>
    /// <param name="signals">The latest signals.</param>
    /// <param name="rest">"When the Lights Rest".</param>
    /// <param name="saverRunning">The Holiday Lights screen saver (or "Preview Screen Saver") is showing.</param>
    /// <returns>The state.</returns>
    public static PauseState Evaluate(PauseSignals signals, RestSettings rest, bool saverRunning)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(rest);
        UserNotificationState state = signals.NotificationState;

        PauseReasons global = PauseReasons.None;
        if (signals.SessionLocked)
        {
            global |= PauseReasons.SessionLocked;
        }

        if (signals.DisplayOff)
        {
            global |= PauseReasons.DisplayOff;
        }

        if (state == UserNotificationState.NotPresent && !saverRunning)
        {
            global |= PauseReasons.UserNotPresent;
        }

        if (saverRunning)
        {
            global |= PauseReasons.OwnScreenSaver;
        }

        if (state == UserNotificationState.PresentationMode && rest.Presentation)
        {
            global |= PauseReasons.Presentation;
        }

        if ((state == UserNotificationState.RunningD3DFullScreen || signals.GameMode) && rest.FullScreen)
        {
            global |= PauseReasons.ExclusiveFullScreen;
        }

        if (signals.RemoteSession)
        {
            global |= PauseReasons.RemoteSession;
        }

        // The saver's own full-screen windows are not "a full-screen app": while it shows, nothing else is in front.
        bool fullScreen = !saverRunning && (signals.FullScreenDisplayIds.Count > 0 || state is UserNotificationState.Busy
            or UserNotificationState.RunningD3DFullScreen or UserNotificationState.PresentationMode || signals.GameMode);
        IReadOnlySet<string> resting = rest.FullScreen && !saverRunning
            ? signals.FullScreenDisplayIds
            : new HashSet<string>();

        bool musicPaused = (fullScreen && rest.MusicFullScreen)
            || (signals.FocusSessionActive && rest.MusicFocus)
            || (signals.SessionLocked && rest.MusicLock && !saverRunning);

        return new PauseState(new LightsPauseState(global, resting), musicPaused, signals.RemoteSession, fullScreen, signals.FocusSessionActive);
    }
}
