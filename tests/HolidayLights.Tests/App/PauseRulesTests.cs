using HolidayLights.App.Shell;
using HolidayLights.Audio;

namespace HolidayLights.Tests.App;

/// <summary>The automatic pausing table of PRODUCT-SPEC 5.12.2 and the music policy built from it (6.1.1, CONTRACTS 6.3).</summary>
public sealed class PauseRulesTests
{
    private static readonly RestSettings Defaults = new();

    private static PauseState Evaluate(PauseSignals signals, RestSettings? rest = null, bool saver = false) =>
        PauseRules.Evaluate(signals, rest ?? Defaults, saver);

    [Fact]
    public void NothingRestsByDefault()
    {
        PauseState state = Evaluate(PauseSignals.None);
        Assert.False(state.Lights.IsGloballyPaused);
        Assert.Empty(state.Lights.RestingDisplayIds);
        Assert.False(state.MusicPausedByRules);
        Assert.False(state.MusicStopped);
        Assert.False(state.SuppressesNotifications);
    }

    [Fact]
    public void LockingHidesTheLightsAndPausesMusic()
    {
        PauseState state = Evaluate(new PauseSignals { SessionLocked = true });
        Assert.True(state.Lights.Global.HasFlag(PauseReasons.SessionLocked));
        Assert.True(state.MusicPausedByRules);

        Assert.False(Evaluate(new PauseSignals { SessionLocked = true }, Defaults with { MusicLock = false }).MusicPausedByRules);

        // The Holiday Lights screen saver keeps playing its music while the PC is locked.
        Assert.False(Evaluate(new PauseSignals { SessionLocked = true }, saver: true).MusicPausedByRules);
    }

    [Fact]
    public void TheDisplayGoingOffStopsTheLightsButNotTheMusic()
    {
        PauseState state = Evaluate(new PauseSignals { DisplayOff = true });
        Assert.True(state.Lights.Global.HasFlag(PauseReasons.DisplayOff));
        Assert.False(state.MusicPausedByRules);
    }

    [Fact]
    public void AnotherScreenSaverStopsTheLightsAndLeavesMusicToItsMode()
    {
        PauseState state = Evaluate(new PauseSignals { NotificationState = UserNotificationState.NotPresent });
        Assert.True(state.Lights.Global.HasFlag(PauseReasons.UserNotPresent));
        Assert.False(state.MusicPausedByRules);
    }

    [Fact]
    public void OurScreenSaverRestsTheDesktopLayers()
    {
        PauseState state = Evaluate(new PauseSignals { NotificationState = UserNotificationState.NotPresent, FullScreenDisplayIds = new HashSet<string> { "display-1" } }, saver: true);
        Assert.Equal(PauseReasons.OwnScreenSaver, state.Lights.Global);
        Assert.Empty(state.Lights.RestingDisplayIds);
        Assert.False(state.MusicPausedByRules);
    }

    [Fact]
    public void AFullScreenAppHidesTheLightsOfItsDisplayOnlyAndPausesMusic()
    {
        var signals = new PauseSignals { FullScreenDisplayIds = new HashSet<string> { "display-2" } };
        PauseState state = Evaluate(signals);
        Assert.Equal(PauseReasons.None, state.Lights.Global);
        Assert.Equal(["display-2"], state.Lights.RestingDisplayIds);
        Assert.True(state.MusicPausedByRules);
        Assert.True(state.SuppressesNotifications);

        PauseState allowed = Evaluate(signals, Defaults with { FullScreen = false, MusicFullScreen = false });
        Assert.Empty(allowed.Lights.RestingDisplayIds);
        Assert.False(allowed.MusicPausedByRules);
    }

    [Fact]
    public void ExclusiveFullScreenAndGameModeHideEveryDisplay()
    {
        Assert.True(Evaluate(new PauseSignals { NotificationState = UserNotificationState.RunningD3DFullScreen }).Lights.Global.HasFlag(PauseReasons.ExclusiveFullScreen));
        PauseState game = Evaluate(new PauseSignals { GameMode = true });
        Assert.True(game.Lights.Global.HasFlag(PauseReasons.ExclusiveFullScreen));
        Assert.True(game.MusicPausedByRules);
        Assert.False(Evaluate(new PauseSignals { GameMode = true }, Defaults with { FullScreen = false }).Lights.IsGloballyPaused);
    }

    [Fact]
    public void PresentationsFollowTheirSetting()
    {
        var signals = new PauseSignals { NotificationState = UserNotificationState.PresentationMode };
        PauseState state = Evaluate(signals);
        Assert.True(state.Lights.Global.HasFlag(PauseReasons.Presentation));
        Assert.True(state.MusicPausedByRules);
        Assert.False(Evaluate(signals, Defaults with { Presentation = false }).Lights.IsGloballyPaused);
    }

    [Fact]
    public void FocusSessionsPauseOnlyTheMusic()
    {
        PauseState state = Evaluate(new PauseSignals { FocusSessionActive = true });
        Assert.False(state.Lights.IsGloballyPaused);
        Assert.True(state.MusicPausedByRules);
        Assert.True(state.SuppressesNotifications);
        Assert.False(Evaluate(new PauseSignals { FocusSessionActive = true }, Defaults with { MusicFocus = false }).MusicPausedByRules);
    }

    [Fact]
    public void RemoteDesktopHidesTheLightsAndStopsTheMusic()
    {
        PauseState state = Evaluate(new PauseSignals { RemoteSession = true });
        Assert.True(state.Lights.Global.HasFlag(PauseReasons.RemoteSession));
        Assert.True(state.MusicStopped);
    }

    [Fact]
    public void TheMusicPolicyCarriesTheSettingsAndTheRules()
    {
        AppSettings settings = new AppSettings() with
        {
            Music = new MusicSettings { Enabled = true, Volume = 35, Muted = true, MidiDevice = "Synth", SyncOffsetMs = 55 },
            Current = new ThemeableSettings { Music = new CurrentMusic { Mode = PlayMode.Intermittently, DisabledSongs = ["bundled:Dreidle.mid"] } },
        };
        PauseState paused = Evaluate(new PauseSignals { FocusSessionActive = true, RemoteSession = true });
        MusicPolicy policy = MusicPolicyRules.Build(settings, paused, saverRunning: true, holdFirstSong: true);

        Assert.True(policy.Enabled);
        Assert.Equal(PlayMode.Intermittently, policy.Mode);
        Assert.Contains("BUNDLED:dreidle.mid", policy.DisabledSongs);
        Assert.Equal(35, policy.Volume);
        Assert.True(policy.Muted);
        Assert.Equal("Synth", policy.MidiDevice);
        Assert.Equal(55, policy.SyncOffsetMs);
        Assert.True(policy.SaverRunning);
        Assert.True(policy.PausedByRules);
        Assert.True(policy.Stopped);
        Assert.True(policy.HoldFirstSong);
    }

    [Fact]
    public void EquivalentPoliciesAreRecognized()
    {
        var settings = new AppSettings();
        MusicPolicy a = MusicPolicyRules.Build(settings, PauseState.None, false, false);
        MusicPolicy b = MusicPolicyRules.Build(settings with { }, PauseState.None, false, false);
        Assert.True(MusicPolicyRules.AreEquivalent(a, b));
        Assert.False(MusicPolicyRules.AreEquivalent(a, MusicPolicyRules.Build(settings, PauseState.None, false, holdFirstSong: true)));
        Assert.False(MusicPolicyRules.AreEquivalent(a, a with { DisabledSongs = new HashSet<string> { "bundled:x.mid" } }));
    }
}
