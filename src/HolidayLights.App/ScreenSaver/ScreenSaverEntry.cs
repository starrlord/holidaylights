using System.Windows.Threading;
using HolidayLights.App.ScreenSaver.FullScreen;
using HolidayLights.App.ScreenSaver.Music;
using HolidayLights.App.ScreenSaver.Preview;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.Platform.Instance;

namespace HolidayLights.App.ScreenSaver;

/// <summary>
/// The screen saver program modes (owner: screensaver; PRODUCT-SPEC 6.2.1, 6.2.2), called by <see cref="Program.Main"/>
/// before the single-instance check (saver processes never take the mutex).
/// </summary>
/// <remarks>
/// <see cref="Program.Main"/> has already created the process's <see cref="App"/> (it shuts down only when told to, and
/// unhandled exceptions go to the rolling log) and an <c>AppHost</c> for <see cref="AppSessionKind.ScreenSaver"/> or
/// <see cref="AppSessionKind.ScreenSaverPreview"/> whose start has run: settings, bulbs, songs, pictures and themes are
/// loaded; there is no mutex, tray, hot keys or presenter, and settings are never written. The entry creates no other
/// <see cref="System.Windows.Application"/>; it runs its own message loop on the calling thread and returns the exit code
/// when the saver ends, after which <see cref="Program"/> stops the host.
/// </remarks>
public static class ScreenSaverEntry
{
    private const string LogSource = "ScreenSaver";

    /// <summary>How long the end of the saver waits for the music to fade out or for Holiday Lights to hear <c>saver-stopped</c>.</summary>
    private static readonly TimeSpan MusicStopTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// <c>/s</c>: one topmost, full-screen, cursor-less window per display of "Show On" (others black). When Holiday Lights
    /// runs, connects to its pipe, sends <c>saver-started</c> and receives music events; otherwise plays the music itself
    /// (switch and mode permitting). Ends on any key, button, pointer movement over 4 DIP or power broadcast, then sends
    /// <c>saver-stopped</c>.
    /// </summary>
    /// <param name="services">The services of the saver process (settings read-only in practice).</param>
    /// <returns>The process exit code.</returns>
    public static int RunFullScreen(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        if (SynchronizationContext.Current is null)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        }

        AppSettings settings = services.Settings.Current;
        var saver = new FullScreenSaver(SaverServices.From(services));
        var running = new DispatcherFrame();
        saver.Ended += (_, _) => running.Continue = false;
        saver.Start(settings, services.Displays.Displays);
        if (!saver.IsRunning)
        {
            // No display to show it on.
            return 0;
        }

        Task<SaverMusicLink?> music = StartMusicAsync(services, settings, saver);
        Dispatcher.PushFrame(running);
        PumpUntil(StopMusicAsync(music), MusicStopTimeout);
        services.Log.Info(LogSource, "Screen saver ended.");
        return 0;
    }

    /// <summary>
    /// <c>/p &lt;hwnd&gt;</c>: a child of Windows' preview window, created on a thread whose DPI awareness equals the
    /// parent's; the saver simulated at the main display's size and drawn scaled; no music. Ends with the parent window.
    /// </summary>
    /// <param name="services">The services of the saver process.</param>
    /// <param name="parentWindow">The preview window handle.</param>
    /// <returns>The process exit code.</returns>
    public static int RunPreview(IAppServices services, nint parentWindow)
    {
        ArgumentNullException.ThrowIfNull(services);
        try
        {
            return new PreviewChildWindow(SaverServices.From(services), services.Settings.Current, services.Displays.Primary, parentWindow).Run();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows' dialog just shows an empty preview.
            services.Log.Error(LogSource, "The screen saver preview couldn't start.", ex);
            return 1;
        }
    }

    /// <summary>Connects to a running Holiday Lights (or starts the local music) and hands the music events to the saver.</summary>
    private static async Task<SaverMusicLink?> StartMusicAsync(IAppServices services, AppSettings settings, FullScreenSaver saver)
    {
        try
        {
            SaverMusicLink link = await SaverMusicLink.StartAsync(
                () => RunningApp(services.Log, services.Paths.DataRoot), services.Music, settings, services.SystemInfo.IsRemoteSession, services.Log);
            saver.AttachMusic(link.Events);
            return link;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The screen saver's music couldn't start.", ex);
            return null;
        }
    }

    private static async Task StopMusicAsync(Task<SaverMusicLink?> start)
    {
        if (await start.ConfigureAwait(false) is { } link)
        {
            await link.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The pipe client when Holiday Lights runs (its mutex exists), else null without waiting. With a data root only an
    /// instance on the same data root counts (<see cref="InstanceNames.MutexFor"/>).
    /// </summary>
    private static SingleInstance? RunningApp(IAppLog log, string? dataRoot)
    {
        try
        {
            if (!Mutex.TryOpenExisting(InstanceNames.MutexFor(dataRoot), out Mutex? mutex))
            {
                return null;
            }

            mutex.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance created it: Holiday Lights runs.
        }

        return new SingleInstance(log, dataRoot);
    }

    /// <summary>Keeps the dispatcher running until a task completes or the timeout passes (nothing blocks the UI thread).</summary>
    private static void PumpUntil(Task task, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        using var timer = new Timer(_ => frame.Continue = false, null, timeout, Timeout.InfiniteTimeSpan);
        if (!task.IsCompleted)
        {
            Dispatcher.PushFrame(frame);
        }
    }
}
