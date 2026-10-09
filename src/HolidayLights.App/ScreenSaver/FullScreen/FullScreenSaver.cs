using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Threading;
using HolidayLights.App.ScreenSaver.Native;
using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// The running full-screen saver (PRODUCT-SPEC 6.2.1, 6.2.2): one top-most window per display (black at once, then the
/// scene as a DirectComposition tree), the 60 ms simulation shown at the refresh rate with Smooth Motion (or 16 steps per
/// second without), until input ends it. Serves both <c>/s</c> and "Preview Screen Saver".
/// </summary>
/// <remarks>UI thread; the caller's dispatcher must be running.</remarks>
internal sealed class FullScreenSaver
{
    private const string LogSource = "ScreenSaver";

    private readonly SaverServices services;
    private readonly List<SaverWindow> windows = [];
    private readonly Dictionary<SaverScene, DisplayView> views = [];
    private SaverCompositor? compositor;
    private SaverOptions? options;
    private IMusicEventSource? music;
    private SaverRun? run;
    private AsyncKeyWatcher? keys;
    private FramePacer? pacer;
    private bool ended;

    /// <summary>Creates the saver.</summary>
    /// <param name="services">What it draws with.</param>
    public FullScreenSaver(SaverServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
    }

    /// <summary>Raised once when the saver ended (its windows are closed).</summary>
    public event EventHandler? Ended;

    /// <summary>True between <see cref="Start"/> and the end.</summary>
    public bool IsRunning => options is not null && !ended;

    /// <summary>Frames shown so far (diagnostics).</summary>
    public long FramesShown { get; private set; }

    /// <summary>Shows the windows and starts the animation.</summary>
    /// <param name="settings">The settings to show.</param>
    /// <param name="displays">The displays, main display first.</param>
    public void Start(AppSettings settings, IReadOnlyList<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(displays);
        if (options is not null)
        {
            throw new InvalidOperationException("The saver was already started.");
        }

        uint seed = unchecked((uint)Stopwatch.GetTimestamp());
        options = SaverOptions.FromSettings(settings, seed);
        IReadOnlyList<SaverDisplayPlan> plans = SaverDisplayPlan.For(displays, options.ShowOn);
        if (plans.Count == 0)
        {
            End();
            return;
        }

        keys = new AsyncKeyWatcher();
        PointI cursor = CursorPosition();
        SaverWindow? main = null;
        foreach (SaverDisplayPlan plan in plans)
        {
            var window = new SaverWindow(plan.Display, plan.ShowsContent ? options.Look.Background : RgbColor.Black, cursor)
            {
                ShowActivated = plan.IsMain,
            };
            window.ExitRequested += (_, _) => End();
            window.Closed += (_, _) =>
            {
                windows.Remove(window);
                End();
            };
            windows.Add(window);
            window.Show();
            main = plan.IsMain ? window : main;
        }

        main?.Activate();
        services.Log.Info(LogSource, $"Screen saver started on {plans.Count} display(s) ({options.ShowOn}).");
        _ = BuildScenesAsync(plans, options, seed);
    }

    /// <summary>Follows music events for "Dance to the Music" (any time, also before the scenes are ready; ignored once ended).</summary>
    /// <param name="events">The events.</param>
    public void AttachMusic(IMusicEventSource events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (ended)
        {
            return;
        }

        music = events;
        run?.ListenTo(events);
    }

    /// <summary>Ends the saver: closes the windows and raises <see cref="Ended"/> (once).</summary>
    public void End()
    {
        if (ended)
        {
            return;
        }

        ended = true;
        pacer?.Dispose();
        pacer = null;
        services.Log.Info(LogSource, $"Screen saver ended after {FramesShown} frames.");

        run?.Dispose();
        foreach (DisplayView view in views.Values)
        {
            view.Dispose();
        }

        views.Clear();
        compositor?.Dispose();
        compositor = null;
        foreach (SaverWindow window in windows.ToArray())
        {
            window.Close();
        }

        Ended?.Invoke(this, EventArgs.Empty);
    }

    private static unsafe PointI CursorPosition()
    {
        NativePoint point;
        return SaverNativeMethods.GetCursorPos(&point) ? new PointI(point.X, point.Y) : default;
    }

    /// <summary>Builds the scenes once the black windows are on screen, then starts drawing.</summary>
    private async Task BuildScenesAsync(IReadOnlyList<SaverDisplayPlan> plans, SaverOptions runOptions, uint seed)
    {
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            DecodedPicture? picture = await Task.Run(() => DecodedPicture.Load(services.Pictures, runOptions.Look.Picture));
            if (ended)
            {
                return;
            }

            long start = Stopwatch.GetTimestamp();
            var renderServices = new SaverRenderServices(services.Bulbs, services.BulbSprites, services.Compositor, new SaverSpriteCache(), services.Log);
            IReadOnlyList<SaverScene> scenes = services.CreateScenes(plans, runOptions, picture, seed, start);
            compositor = SaverCompositor.Create();
            var shown = new List<SaverScene>();
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].Plan.ShowsContent)
                {
                    views[scenes[i]] = new DisplayView(scenes[i], renderServices, compositor, new WindowInteropHelper(windows[i]).Handle);
                    shown.Add(scenes[i]);
                }
            }

            run = new SaverRun(shown, start);
            run.ListenTo(music);
            pacer = new FramePacer(Dispatcher.CurrentDispatcher, OnFrame);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The windows stay black and still end on input.
            services.Log.Error(LogSource, "The screen saver couldn't draw its scene.", ex);
        }
    }

    /// <summary>One compositor frame: steps due, music, bulbs, sprite positions, one commit.</summary>
    private void OnFrame()
    {
        if (run is null || options is null || ended)
        {
            return;
        }

        if (keys?.NewPress() == true)
        {
            End();
            return;
        }

        try
        {
            FramesShown++;
            int steps = run.Advance(Stopwatch.GetTimestamp(), scene => views[scene].ApplyStep());
            bool moved = options.SmoothMotion || steps > 0;
            foreach (DisplayView view in views.Values)
            {
                view.RenderFrame(run.Progress, moved);
            }

            if (compositor?.Commit() == false)
            {
                // The graphics device was lost (driver update, GPU reset): Windows starts the saver again when idle.
                services.Log.Warn(LogSource, "The graphics device was lost; the screen saver ends.");
                End();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "Drawing the screen saver failed; it stops animating.", ex);
            pacer?.Dispose();
            pacer = null;
        }
    }
}
