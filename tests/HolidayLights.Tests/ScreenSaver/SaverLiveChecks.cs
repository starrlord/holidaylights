using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HolidayLights.App.ScreenSaver;
using HolidayLights.App.ScreenSaver.FullScreen;
using HolidayLights.App.ScreenSaver.Music;
using HolidayLights.Audio;
using HolidayLights.Platform.Displays;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>
/// Runs only with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>: shows the real saver on the real displays for a few seconds (the
/// default settings, or the animation and flash pattern named by <c>HOLIDAYLIGHTS_SAVER_ANIMATION</c> and
/// <c>HOLIDAYLIGHTS_SAVER_PATTERN</c>). Every check closes its windows itself.
/// </summary>
public sealed class SaverLiveFactAttribute : FactAttribute
{
    public const string AnimationVariable = "HOLIDAYLIGHTS_SAVER_ANIMATION";

    public const string PatternVariable = "HOLIDAYLIGHTS_SAVER_PATTERN";

    public SaverLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HOLIDAYLIGHTS_LIVE_TESTS") != "1")
        {
            Skip = "Live screen saver test: set HOLIDAYLIGHTS_LIVE_TESTS=1 to run it (don't touch the mouse or keyboard).";
        }
    }
}

/// <summary>The saver on the real displays: full screen, <c>/s</c>, "Preview Screen Saver", <c>/p</c> and the Settings preview.</summary>
[Trait("Category", "Live")]
public sealed class SaverLiveChecks
{
    private readonly ITestOutputHelper output;

    public SaverLiveChecks(ITestOutputHelper output)
    {
        this.output = output;
        SaverTestScreen.UsePhysicalPixels();
    }

    [SaverLiveFact]
    public void FullScreenSaver_CoversEveryDisplay_AtTheRefreshRate()
    {
        var captures = new List<(DisplayInfo Display, uint[] Pixels)>();
        bool ended = false;
        double cpuPercent = 0;
        double framesPerSecond = 0;
        StaThread.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            AppSettings settings = LiveSettings();
            using var kit = new SaverTestKit(settings);
            using var displays = new DisplayService(kit.Log);
            var saver = new FullScreenSaver(kit.Services);
            saver.Ended += (_, _) => ended = true;
            var done = new DispatcherFrame();
            TimeSpan warmCpu = default;
            long warmFrames = 0;
            var watch = new Stopwatch();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) =>
            {
                if (!watch.IsRunning)
                {
                    // Measure after the start: scenes, sprites and the JIT are ready.
                    warmCpu = Process.GetCurrentProcess().TotalProcessorTime;
                    warmFrames = saver.FramesShown;
                    watch.Start();
                    return;
                }

                timer.Stop();
                cpuPercent = (Process.GetCurrentProcess().TotalProcessorTime - warmCpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100;
                framesPerSecond = (saver.FramesShown - warmFrames) / watch.Elapsed.TotalSeconds;
                captures.AddRange(displays.Displays.Select(d => (d, SaverTestScreen.Capture(d.Bounds))));
                saver.End();
                done.Continue = false;
            };

            saver.Start(settings, displays.Displays);
            timer.Start();
            Dispatcher.PushFrame(done);
        }, TimeSpan.FromSeconds(30));

        output.WriteLine($"Steady state: {cpuPercent:F1} % of one core, {framesPerSecond:F0} frames per second.");
        Assert.True(ended);
        Assert.True(framesPerSecond > 45, "The saver should keep pace with the display.");
        Assert.NotEmpty(captures);
        foreach ((DisplayInfo display, uint[] pixels) in captures)
        {
            int lit = SaverTestScreen.CountLit(pixels);
            output.WriteLine($"{display.DeviceName} {display.Bounds}: {lit} non-black pixels.");
            Assert.True(lit > pixels.Length / 200, $"{display.DeviceName} shows almost nothing.");
            if (SaverFramesFactAttribute.Folder is { } folder)
            {
                SaverTestScreen.Save(Path.Combine(folder, $"live-{display.Number}.png"), pixels, display.Bounds.Width, display.Bounds.Height);
            }
        }
    }

    [SaverLiveFact]
    public void RunFullScreen_PlaysItsOwnMusic_AndEndsOnAKeyPress()
    {
        if (Mutex.TryOpenExisting(InstanceNames.Mutex, out Mutex? running))
        {
            running.Dispose();
            output.WriteLine("Holiday Lights is running here: the check would talk to it, so it does not run.");
            return;
        }

        var music = new FakeDirector();
        var sessions = new RecordingSessions();
        int exitCode = -1;
        bool pressed = false;
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            using var displays = new DisplayService(kit.Log);
            using var press = new Timer(_ => pressed = SaverTestScreen.PressKeyInSaverWindow(), null, TimeSpan.FromSeconds(2.5), Timeout.InfiniteTimeSpan);
            exitCode = ScreenSaverEntry.RunFullScreen(new SaverAppServices(kit, displays, music, sessions));
        }, TimeSpan.FromSeconds(30));

        Assert.True(pressed);
        Assert.Equal(0, exitCode);
        MusicPolicy policy = Assert.Single(music.Policies);
        Assert.True(policy.SaverRunning);
        Assert.Equal(SaverMusicLink.FadeOut, music.StoppedWith);
        Assert.Equal(0, sessions.Started);
    }

    [SaverLiveFact]
    public void PreviewScreenSaver_RunsInProcess_AndTellsTheSessions()
    {
        var sessions = new RecordingSessions();
        bool runningMeanwhile = false;
        bool runningAfter = true;
        StaThread.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var kit = new SaverTestKit();
            using var displays = new DisplayService(kit.Log);
            var service = new ScreenSaverService(new SaverAppServices(kit, displays, new FakeDirector(), sessions));

            Task preview = service.RunPreviewAsync();
            runningMeanwhile = service.IsPreviewRunning;
            Assert.Same(preview, service.RunPreviewAsync());
            using var press = new Timer(_ => SaverTestScreen.PressKeyInSaverWindow(), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            var done = new DispatcherFrame();
            preview.ContinueWith(_ => done.Continue = false, TaskScheduler.Default);
            Dispatcher.PushFrame(done);
            runningAfter = service.IsPreviewRunning;
        }, TimeSpan.FromSeconds(30));

        Assert.True(runningMeanwhile);
        Assert.False(runningAfter);
        Assert.Equal((1, 1), (sessions.Started, sessions.Stopped));
    }

    [SaverLiveFact]
    public void WindowsPreview_DrawsInsideItsParentWindow_AndEndsWithIt()
    {
        // A window on another thread stands in for the preview monitor of Windows' Screen Saver Settings dialog.
        using var ready = new ManualResetEventSlim();
        Window? parent = null;
        nint parentHandle = 0;
        RectI area = default;
        var parentThread = new Thread(() =>
        {
            parent = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowActivated = false, ShowInTaskbar = false,
                Left = 120, Top = 120, Width = 320, Height = 180, Background = Brushes.Gray,
            };
            parent.Show();
            parentHandle = new WindowInteropHelper(parent).Handle;
            Point topLeft = parent.PointToScreen(new Point(0, 0));
            Point bottomRight = parent.PointToScreen(new Point(parent.ActualWidth, parent.ActualHeight));
            area = new RectI((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
            ready.Set();
            Dispatcher.Run();
        });
        parentThread.SetApartmentState(ApartmentState.STA);
        parentThread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));

        int exitCode = -1;
        uint[]? pixels = null;
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            using var displays = new DisplayService(kit.Log);
            using var close = new Timer(_ =>
            {
                pixels = SaverTestScreen.Capture(area);
                parent!.Dispatcher.Invoke(parent.Close);
            }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            exitCode = ScreenSaverEntry.RunPreview(new SaverAppServices(kit, displays, new FakeDirector(), new RecordingSessions()), parentHandle);
        }, TimeSpan.FromSeconds(30));
        parent!.Dispatcher.InvokeShutdown();
        parentThread.Join();

        Assert.Equal(0, exitCode);
        Assert.NotNull(pixels);
        uint gray = Colors.Gray.R;
        int parentShowing = pixels.Count(p => (p & 0xFF) == gray && (p >> 8 & 0xFF) == gray && (p >> 16 & 0xFF) == gray);
        output.WriteLine($"/p: {SaverTestScreen.CountLit(pixels)} non-black and {parentShowing} gray pixels of {pixels.Length}.");
        Assert.True(parentShowing < pixels.Length / 10, "The preview should cover its parent window.");
        Assert.True(SaverTestScreen.CountLit(pixels) > pixels.Length / 100, "The preview should show the bulbs and the picture.");
        if (SaverFramesFactAttribute.Folder is { } folder)
        {
            SaverTestScreen.Save(Path.Combine(folder, "live-preview-p.png"), pixels, area.Width, area.Height);
        }
    }

    [SaverLiveFact]
    public void SettingsPreview_ShowsTheMainDisplaysSaverInItsShape()
    {
        uint[]? pixels = null;
        RectI area = default;
        StaThread.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            using var kit = new SaverTestKit();
            using var displays = new DisplayService(kit.Log);
            var service = new ScreenSaverService(new SaverAppServices(kit, displays, new FakeDirector(), new RecordingSessions()));
            FrameworkElement preview = service.CreatePreview();
            var window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowActivated = false, ShowInTaskbar = false,
                Left = 120, Top = 120, Width = 640, Height = 400, Background = Brushes.Gray, Content = preview,
            };
            var done = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Point topLeft = preview.PointToScreen(new Point(0, 0));
                Point bottomRight = preview.PointToScreen(new Point(preview.ActualWidth, preview.ActualHeight));
                area = new RectI((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
                pixels = SaverTestScreen.Capture(area);
                window.Close();
                done.Continue = false;
            };
            window.Show();
            timer.Start();
            Dispatcher.PushFrame(done);
        }, TimeSpan.FromSeconds(30));

        Assert.NotNull(pixels);
        int lit = SaverTestScreen.CountLit(pixels);
        output.WriteLine($"Settings preview {area}: {lit} non-black pixels of {pixels.Length}.");
        Assert.True(lit > pixels.Length / 100);
        if (SaverFramesFactAttribute.Folder is { } folder)
        {
            SaverTestScreen.Save(Path.Combine(folder, "live-settings-preview.png"), pixels, area.Width, area.Height);
        }
    }

    private static AppSettings LiveSettings() => new()
    {
        Current = new ThemeableSettings
        {
            Saver = new SaverLook
            {
                Animation = Environment.GetEnvironmentVariable(SaverLiveFactAttribute.AnimationVariable) is { Length: > 0 } animation ? animation : SaverAnimations.Snow,
            },
            Flash = new FlashSettings
            {
                Pattern = Enum.TryParse(Environment.GetEnvironmentVariable(SaverLiveFactAttribute.PatternVariable), out FlashPatternId pattern)
                    ? pattern
                    : FlashPatternId.FlashTogether,
            },
        },
    };
}
