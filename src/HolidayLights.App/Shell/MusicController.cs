using System.Windows.Threading;
using HolidayLights.Audio;

namespace HolidayLights.App.Shell;

/// <summary>
/// Applies the music policy of the running app (CONTRACTS 6.3): on every change of <c>music.*</c>,
/// <c>current.music</c>, the pause rules, the screen saver sessions, the Remote Desktop state and the held first song,
/// <see cref="IMusicDirector.ApplyPolicy"/> receives the new policy. Also shows "Music is waiting" when another app holds
/// the MIDI synthesizer (once per session, not while the Music Box page shows it). UI thread.
/// </summary>
public sealed class MusicController : IDisposable
{
    private const string LogSource = "Shell.Music";

    private readonly IAppServices services;
    private readonly IMusicDirector music;
    private readonly ISettingsStore settings;
    private readonly PauseMonitor pause;
    private readonly ScreenSaverSessions sessions;
    private readonly Dispatcher dispatcher;
    private MusicPolicy? applied;
    private MusicStatus lastStatus = MusicStatus.Off;
    private int holds;
    private bool started;
    private bool disposed;

    /// <summary>Creates the controller; call <see cref="Start"/>.</summary>
    /// <param name="services">The services (music, settings, notifications, the Settings window).</param>
    /// <param name="pause">The pause aggregation.</param>
    /// <param name="sessions">The screen saver sessions.</param>
    public MusicController(IAppServices services, PauseMonitor pause, ScreenSaverSessions sessions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(sessions);
        this.services = services;
        this.pause = pause;
        this.sessions = sessions;
        music = services.Music;
        settings = services.Settings;
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>True while the first song is held (the Welcome card is open).</summary>
    public bool IsHolding => holds > 0;

    /// <summary>Starts following the conditions and applies the first policy.</summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        settings.Changed += OnSettingsChanged;
        pause.Changed += OnConditionsChanged;
        sessions.Changed += OnConditionsChanged;
        music.StateChanged += OnMusicStateChanged;
        Apply();
    }

    /// <summary>Holds the first song until the returned object is disposed (5.4 imports and the Welcome card, PRODUCT-SPEC 6.1.2).</summary>
    /// <returns>Dispose to release the hold.</returns>
    public IDisposable HoldFirstSong()
    {
        holds++;
        Apply();
        return new Hold(this);
    }

    /// <summary>Stops following the conditions (the director is stopped by the composition root).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (started)
        {
            settings.Changed -= OnSettingsChanged;
            pause.Changed -= OnConditionsChanged;
            sessions.Changed -= OnConditionsChanged;
            music.StateChanged -= OnMusicStateChanged;
        }
    }

    private void Release()
    {
        holds = Math.Max(0, holds - 1);
        Apply();
    }

    private void Apply()
    {
        if (!started || disposed)
        {
            return;
        }

        MusicPolicy policy = MusicPolicyRules.Build(settings.Current, pause.Current, sessions.IsSaverRunning, holds > 0);
        if (applied is not null && MusicPolicyRules.AreEquivalent(applied, policy))
        {
            return;
        }

        applied = policy;
        music.ApplyPolicy(policy);
        services.Log.Write(AppLogLevel.Debug, LogSource,
            $"Music policy: enabled {policy.Enabled}, {policy.Mode}, saver {policy.SaverRunning}, paused {policy.PausedByRules}, stopped {policy.Stopped}, hold {policy.HoldFirstSong}.");
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Music != e.NewSettings.Music || e.OldSettings.Current.Music != e.NewSettings.Current.Music)
        {
            Apply();
        }
    }

    private void OnConditionsChanged(object? sender, EventArgs e) => Apply();

    private void OnMusicStateChanged(object? sender, EventArgs e)
    {
        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(NotifyWhenWaiting);
        }
    }

    /// <summary>"Music is waiting" (PRODUCT-SPEC 3.12): once per session, only while Music Box does not show the status.</summary>
    private void NotifyWhenWaiting()
    {
        MusicStatus status = music.State.Status;
        bool entered = status == MusicStatus.WaitingForSynthesizer && lastStatus != MusicStatus.WaitingForSynthesizer;
        lastStatus = status;
        bool musicBoxOpen = services.SettingsWindow.CurrentPage == SettingsPageId.MusicBox;
        if (entered && !musicBoxOpen)
        {
            services.Notifications.Show(NotificationKind.MusicWaiting, "Music is waiting", "Another app is using the MIDI synthesizer. Holiday Lights will try again.");
        }
    }

    private sealed class Hold(MusicController owner) : IDisposable
    {
        private MusicController? controller = owner;

        public void Dispose()
        {
            controller?.Release();
            controller = null;
        }
    }
}
