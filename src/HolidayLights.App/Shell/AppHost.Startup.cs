using System.IO;
using System.Windows;
using System.Windows.Threading;
using HolidayLights.App.FirstRun;
using HolidayLights.App.Tray;

namespace HolidayLights.App.Shell;

/// <summary>The start-up flows of the session kinds (CONTRACTS 6.1; PRODUCT-SPEC 2.5).</summary>
public sealed partial class AppHost
{
    /// <summary>The Welcome card opens this long after the first lights (after the power-up wave, PRODUCT-SPEC 2.5.1).</summary>
    private static readonly TimeSpan WelcomeDelay = TimeSpan.FromSeconds(2.5);

    /// <summary>How often the presenter's diagnostics are written to the log.</summary>
    private static readonly TimeSpan DiagnosticsInterval = TimeSpan.FromHours(1);

    /// <summary>The first diagnostics entry follows the power-up.</summary>
    private static readonly TimeSpan FirstDiagnosticsDelay = TimeSpan.FromSeconds(30);

    private readonly ISingleInstance? singleInstance;
    private TrayIconController? tray;
    private MusicController? musicController;
    private AutomaticThemeScheduler? scheduler;
    private InstanceServer? instanceServer;
    private SystemIntegrationController? integration;
    private IDisposable? startupMusicHold;
    private DispatcherTimer? diagnosticsTimer;

    /// <summary>
    /// True from the start of a first run (no settings file yet) until its newcomer or 5.4-import settings are applied: a
    /// session that ends before then writes no settings file, so the next start is a first run again.
    /// </summary>
    private bool firstRunPending;

    /// <summary>Runs the start-up flow for a launch (UI thread, after <see cref="App"/> exists).</summary>
    /// <param name="request">The parsed command line.</param>
    /// <returns>A task that completes when the session is up (lights committed, tray shown).</returns>
    public Task StartAsync(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log.Info(LogSource, $"Holiday Lights {VersionInfo.ProgramVersion} starting: {Options.Session}, {request.Kind}" +
            $"{(Options.AllowSystemChanges ? "" : ", no system changes")}{(Paths.DataRoot is null ? "" : ", data root")}.");
        return Options.Session switch
        {
            AppSessionKind.Normal => StartNormalAsync(request),
            AppSessionKind.SettingsOnly => StartSettingsOnlyAsync(),
            AppSessionKind.ScreenSaver or AppSessionKind.ScreenSaverPreview => StartContent(),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>The normal app: settings, content, the first run, lights first, then the tray, hot keys, pipe, music, Automatic themes and the Welcome card.</summary>
    private async Task StartNormalAsync(LaunchRequest request)
    {
        ISettingsStore store = Settings;
        SettingsLoadOutcome outcome = store.LoadOutcome;
        firstRunPending = outcome == SettingsLoadOutcome.Created;
        _ = Displays;
        _ = SystemInfo;
        _ = Task.Run(RecycleLeftovers);
        await StartContent().ConfigureAwait(true);

        FirstRunOutcome? firstRun = firstRunPending ? await new FirstRunSetup(this, time).RunAsync().ConfigureAwait(true) : null;
        firstRunPending = false;

        // Lights first (PRODUCT-SPEC 1.2 #1): nothing else is shown before them.
        PauseMonitor pause = pauseMonitor.Value!;
        pause.Start();
        lights.Value.Start(FirstTransition(firstRun), ProcessStartTimestamp);

        tray = new TrayIconController(this, () => pause.Current.Lights, notifications.Value.OnClicked, time);
        tray.Show();
        pause.Changed += (_, _) => tray?.Refresh();
        hotKeys.Value.Start();
        var router = new InstanceCommandRouter(this, hotKeys.Value, time);
        if (singleInstance is not null)
        {
            instanceServer = new InstanceServer(singleInstance, router, saverSessions.Value, Music, Log);
            instanceServer.Start();
        }

        // A settings file that could not be read yet (review r1 #6) holds the user's real choices: the defaults in its place
        // neither change Start with Windows or the .bul association nor bring up the Welcome card.
        bool unavailable = outcome == SettingsLoadOutcome.Unavailable;
        integration = new SystemIntegrationController(store, Startup, FileAssociation, Log);
        _ = integration.Start(reconcile: !unavailable);
        musicController = new MusicController(this, pause, saverSessions.Value);
        bool welcomePending = !unavailable && !store.Current.Onboarding.WelcomeShown && !Options.StartedAtSignIn;
        WelcomeVariant welcome = firstRun?.Variant ?? VariantFromImport(store.Current);
        if (firstRun?.HoldFirstSong == true || (welcomePending && welcome != WelcomeVariant.Newcomer))
        {
            // A 5.4 import's first song waits for its Welcome card, also when the import ran earlier (at sign-in or in
            // a settings-only session) and the card opens only now.
            startupMusicHold = musicController.HoldFirstSong();
        }

        musicController.Start();
        scheduler = new AutomaticThemeScheduler(this, notifications.Value, time);
        scheduler.Start();
        StartDiagnosticsLog();

        if (outcome == SettingsLoadOutcome.ReplacedDamaged)
        {
            Notifications.Show(NotificationKind.SettingsReset, "Settings were reset",
                "Your settings file couldn't be read, so Holiday Lights started with defaults. Your themes and bulbs are safe.");
        }
        else if (unavailable)
        {
            Notifications.Show(NotificationKind.SettingsReset, "Settings couldn't be read",
                "Another program is using your settings file, so Holiday Lights started with defaults. Your settings will be used as soon as the file can be read; nothing was changed.");
        }

        if (welcomePending)
        {
            _ = ShowWelcomeLaterAsync(welcome);
        }
        else
        {
            ReleaseStartupHold();
        }

        if (request.Kind is not (LaunchKind.Normal or LaunchKind.Autostart))
        {
            router.Execute(LaunchCommands.ForRunningInstance(request)!);
        }
    }

    /// <summary>The first transition: the first-run power-up, the sign-in power-up, or the short one.</summary>
    private SceneTransition FirstTransition(FirstRunOutcome? firstRun) =>
        firstRun is not null ? SceneTransition.FirstRunPowerUp
            : Options.StartedAtSignIn ? SceneTransition.AutostartPowerUp
            : SceneTransition.ShortPowerUp;

    /// <summary>The Welcome variant of a session whose first run was earlier (at sign-in or in a settings-only session; the card waited for a start by hand).</summary>
    private static WelcomeVariant VariantFromImport(AppSettings settings) => settings.Import54 switch
    {
        null => WelcomeVariant.Newcomer,
        { FactoryDefaults: true } => WelcomeVariant.Imported54FactoryDefaults,
        _ => WelcomeVariant.Imported54Customized,
    };

    /// <summary>Shows the Welcome card once, after the power-up; no song starts while it is open.</summary>
    private async Task ShowWelcomeLaterAsync(WelcomeVariant variant)
    {
        try
        {
            await Task.Delay(WelcomeDelay).ConfigureAwait(true);
            if (disposed)
            {
                return;
            }

            Settings.Update(s => s with { Onboarding = s.Onboarding with { WelcomeShown = true } }, SettingsChange.Internal);
            using (musicController?.HoldFirstSong())
            {
                ReleaseStartupHold();
                await WelcomeCard.ShowAsync(this, variant).ConfigureAwait(true);
            }
        }
        catch (Exception e)
        {
            // A broken card must never hold the music or take the lights down.
            Log.Error(LogSource, "The Welcome card could not be shown.", e);
            ReleaseStartupHold();
        }
    }

    private void ReleaseStartupHold()
    {
        startupMusicHold?.Dispose();
        startupMusicHold = null;
    }

    /// <summary>A settings-only session (screen saver "Settings" button, legacy <c>settings</c>): Settings on Screen Saver; the program ends with the window.</summary>
    /// <remarks>
    /// On a profile without a settings file the first-run settings (the newcomer settings or the Holiday Lights 5.4 import,
    /// PRODUCT-SPEC 2.5, 6.8.1) are applied before the window opens: the settings this session writes are then never plain
    /// defaults that would make the next start skip the first run. The Welcome card still waits for the first normal start
    /// (<c>onboarding.welcomeShown</c> stays false). A session that ends before the first run finished writes nothing.
    /// </remarks>
    private async Task StartSettingsOnlyAsync()
    {
        if (Settings.LoadOutcome == SettingsLoadOutcome.Created)
        {
            firstRunPending = true;
            await StartContent().ConfigureAwait(true);
            FirstRunOutcome firstRun = await new FirstRunSetup(this, time).RunAsync().ConfigureAwait(true);
            firstRunPending = false;
            Log.Info(LogSource, $"First run in a settings-only session ({firstRun.Variant}); the Welcome card waits for the first normal start.");
        }

        integration = new SystemIntegrationController(Settings, Startup, FileAssociation, Log);
        _ = integration.Start(reconcile: false);
        ISettingsWindowService window = SettingsWindow;
        window.Closed += async (_, _) =>
        {
            await DisposeAsync().ConfigureAwait(true);
            Application.Current?.Shutdown();
        };
        window.Show(SettingsPageId.ScreenSaver);
    }

    /// <summary>The content every session shows: settings, bulbs (indexing starts), songs, pictures and themes.</summary>
    private Task StartContent()
    {
        _ = Settings;
        _ = Bulbs;
        _ = Songs;
        _ = Pictures;
        _ = Themes;
        return Task.CompletedTask;
    }

    /// <summary>Sends files left in the holding folder by an earlier crash to the Recycle Bin (thread pool).</summary>
    private void RecycleLeftovers()
    {
        try
        {
            Holding.RecycleLeftovers();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogSource, "Removed files of an earlier session could not be recycled.", e);
        }
    }

    /// <summary>Writes the presenter's diagnostics (adapter, desktop structure, layers, commits, CPU time) to the log after the power-up and every hour.</summary>
    private void StartDiagnosticsLog()
    {
        diagnosticsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstDiagnosticsDelay };
        diagnosticsTimer.Tick += (_, _) =>
        {
            diagnosticsTimer.Interval = DiagnosticsInterval;
            LogDiagnostics();
        };
        diagnosticsTimer.Start();
    }

    private void LogDiagnostics()
    {
        if (lights.Value.Diagnostics is not { } d)
        {
            return;
        }

        Log.Info(LogSource,
            $"Lights diagnostics: adapter \"{d.Adapter}\"{(d.SoftwareRendering ? " (software)" : "")}, desktop {d.DesktopLayout}, " +
            $"{d.Layers.Count} layer(s), {d.Steps} steps, {d.Commits} commits, CPU {d.ThreadCpuTime.TotalSeconds:0.00} s in {d.Uptime.TotalMinutes:0} min, " +
            $"max step latency {d.MaxStepLatencyMilliseconds:0.0} ms, {d.Surfaces} surfaces, {d.Visuals} visuals, {d.DeviceLosses} device losses.");
    }
}
