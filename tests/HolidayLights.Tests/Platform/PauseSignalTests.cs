using HolidayLights.Platform.Displays;
using HolidayLights.Platform.Power;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

public sealed class PauseSignalTests
{
    private static readonly DisplayInfo Main = DisplayChangesTests.Display("main", 1, new RectI(0, 0, 3840, 2160)) with
    {
        WorkArea = new RectI(0, 0, 3840, 2088),
    };

    private static readonly DisplayInfo Left = DisplayChangesTests.Display("left", 2, new RectI(-3840, 0, 0, 2160));

    [Fact]
    public void CoveredDisplays_OnlyWhenTheWholeDisplayIsCovered()
    {
        DisplayInfo[] displays = [Main, Left];

        Assert.Equal(["left"], FullScreenDetector.CoveredDisplays(new RectI(-3840, 0, 0, 2160), displays));
        Assert.Equal(["main"], FullScreenDetector.CoveredDisplays(new RectI(-10, -10, 3850, 2170), displays));
        Assert.True(FullScreenDetector.CoveredDisplays(new RectI(-3840, 0, 3840, 2160), displays).SetEquals(["main", "left"]));

        // A maximized window covers only the work area.
        Assert.Empty(FullScreenDetector.CoveredDisplays(Main.WorkArea, displays));
        Assert.Empty(FullScreenDetector.CoveredDisplays(new RectI(0, 0, 3840, 2159), displays));
    }

    [Fact]
    public void MainDisplay_IsTheDisplayThatShowsMostOfTheWindow()
    {
        DisplayInfo[] displays = [Main, Left];

        Assert.Equal("left", FullScreenDetector.MainDisplay(new RectI(-1000, 100, 200, 900), displays)?.DeviceId);
        Assert.Equal("main", FullScreenDetector.MainDisplay(new RectI(-200, 100, 1000, 900), displays)?.DeviceId);
        Assert.Null(FullScreenDetector.MainDisplay(new RectI(5000, 0, 6000, 100), displays));
    }

    [Fact]
    public void Detect_TheForegroundWindowMakesItsDisplaysFullScreen()
    {
        var windows = new FakeWindows();
        var detector = new FullScreenDetector(windows);
        DisplayInfo[] displays = [Main, Left];

        windows.Show(1, Left.Bounds, foreground: true);
        Assert.Equal(["left"], detector.Detect(displays));

        // A maximized window covers only the work area; our own windows and shell windows never count.
        windows.Show(2, Main.WorkArea, foreground: true);
        windows.Hide(1);
        Assert.Empty(detector.Detect(displays));
        windows.Show(3, Main.Bounds, foreground: true, own: true);
        Assert.Empty(detector.Detect(displays));
        windows.Foreground = 99;
        Assert.Empty(detector.Detect(displays));
    }

    [Fact]
    public void Detect_KeepsAFullScreenAppAfterTheUserMovesToAnotherDisplay()
    {
        var windows = new FakeWindows();
        var detector = new FullScreenDetector(windows);
        DisplayInfo[] displays = [Main, Left];

        // A full-screen video on the left display, then the user works in a window on the main display (review r1 #78).
        windows.Show(1, Left.Bounds, foreground: true);
        Assert.Equal(["left"], detector.Detect(displays));
        windows.Show(2, new RectI(100, 100, 1500, 1000), foreground: true);
        Assert.Equal(["left"], detector.Detect(displays));

        // Clicking the desktop, the taskbar or a shell overlay (Inspect is null for them) changes nothing.
        windows.Foreground = 99;
        Assert.Equal(["left"], detector.Detect(displays));

        // A full-screen app on the main display too: both rest.
        windows.Show(3, Main.Bounds, foreground: true);
        Assert.True(detector.Detect(displays).SetEquals(["main", "left"]));

        // Our own window on the main display hides what was full screen there, not the left display.
        windows.Show(4, new RectI(200, 200, 900, 900), foreground: true, own: true);
        Assert.Equal(["left"], detector.Detect(displays));
        windows.Show(2, new RectI(100, 100, 1500, 1000), foreground: true);
        Assert.Equal(["left"], detector.Detect(displays));
    }

    [Fact]
    public void Detect_ForgetsTheFullScreenAppWhenItStopsCoveringItsDisplay()
    {
        var windows = new FakeWindows();
        var detector = new FullScreenDetector(windows);
        DisplayInfo[] displays = [Main, Left];
        var work = new RectI(100, 100, 1500, 1000);

        // Minimized, closed or moved to another virtual desktop (Inspect is null then).
        windows.Show(1, Left.Bounds, foreground: true);
        detector.Detect(displays);
        windows.Show(2, work, foreground: true);
        windows.Hide(1);
        Assert.Empty(detector.Detect(displays));
        windows.Show(1, Left.Bounds);
        Assert.Empty(detector.Detect(displays));

        // Out of full screen.
        windows.Show(1, Left.Bounds, foreground: true);
        detector.Detect(displays);
        windows.Show(2, work, foreground: true);
        windows.Show(1, new RectI(-3000, 100, -1000, 900));
        Assert.Empty(detector.Detect(displays));
        windows.Show(1, Left.Bounds);
        Assert.Empty(detector.Detect(displays));

        // The display is gone.
        windows.Show(1, Left.Bounds, foreground: true);
        detector.Detect(displays);
        windows.Show(2, work, foreground: true);
        Assert.Empty(detector.Detect([Main]));
        Assert.Empty(detector.Detect(displays));
    }

    [Fact]
    public void Detect_ForgetsTheFullScreenAppWhenTheUserActivatesAnotherWindowOnItsDisplay()
    {
        var windows = new FakeWindows();
        var detector = new FullScreenDetector(windows);
        DisplayInfo[] displays = [Main, Left];

        windows.Show(1, Left.Bounds, foreground: true);
        detector.Detect(displays);

        // A window mostly on the left display (it spills onto the main one) is in front of the video now.
        windows.Show(2, new RectI(-2000, 100, 300, 900), foreground: true);
        Assert.Empty(detector.Detect(displays));
        windows.Show(3, new RectI(100, 100, 1500, 1000), foreground: true);
        Assert.Empty(detector.Detect(displays));

        // A window mostly on the main display that spills onto the left one does not.
        windows.Show(1, Left.Bounds, foreground: true);
        detector.Detect(displays);
        windows.Show(2, new RectI(-300, 100, 2000, 900), foreground: true);
        Assert.Equal(["left"], detector.Detect(displays));
    }

    [Fact]
    public void Detect_IgnoresWindowsThatCoverADisplayWithoutEverHavingTheFocus()
    {
        // Transparent overlays (game and recording overlays, screen-sharing borders) cover whole displays.
        var windows = new FakeWindows();
        var detector = new FullScreenDetector(windows);
        DisplayInfo[] displays = [Main, Left];

        windows.Show(1, new RectI(-3840, 0, 3840, 2160));
        windows.Show(2, new RectI(100, 100, 1500, 1000), foreground: true);

        Assert.Empty(detector.Detect(displays));
    }

    [Fact]
    public void PollPeriod_SlowsToTwoSecondsWhileTheLightsRestEverywhere()
    {
        // PRODUCT-SPEC 5.12.2 and 5.14: while everything rests, only a 2 s state poll runs (review r1 #77).
        TimeSpan normal = TimeSpan.FromSeconds(1);
        TimeSpan resting = TimeSpan.FromSeconds(2);

        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None));
        Assert.Equal(resting, PauseSignalSource.PollPeriodFor(PauseSignals.None with { SessionLocked = true }));
        Assert.Equal(resting, PauseSignalSource.PollPeriodFor(PauseSignals.None with { DisplayOff = true }));
        Assert.Equal(resting, PauseSignalSource.PollPeriodFor(PauseSignals.None with { RemoteSession = true }));
        Assert.Equal(resting, PauseSignalSource.PollPeriodFor(PauseSignals.None with { NotificationState = UserNotificationState.NotPresent }));

        // These hide the lights only by the user's settings, or only on some displays, or only pause music.
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { NotificationState = UserNotificationState.Busy }));
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { NotificationState = UserNotificationState.RunningD3DFullScreen }));
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { NotificationState = UserNotificationState.PresentationMode }));
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { FullScreenDisplayIds = new HashSet<string> { "main" } }));
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { GameMode = true }));
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { EnergySaverOn = true }));
        Assert.Equal(normal, PauseSignalSource.PollPeriodFor(PauseSignals.None with { FocusSessionActive = true }));
    }

    [Fact]
    public void SameSignals_ComparesTheFullScreenSetsByContent()
    {
        var a = new PauseSignals { FullScreenDisplayIds = new HashSet<string> { "left", "main" } };
        var b = new PauseSignals { FullScreenDisplayIds = new HashSet<string> { "main", "left" } };

        Assert.True(PauseSignalSource.SameSignals(a, b));
        Assert.True(PauseSignalSource.SameSignals(PauseSignals.None, new PauseSignals()));
        Assert.False(PauseSignalSource.SameSignals(a, a with { FullScreenDisplayIds = new HashSet<string> { "main" } }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { SessionLocked = true }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { DisplayOff = true }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { NotificationState = UserNotificationState.PresentationMode }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { EnergySaverOn = true }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { GameMode = true }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { FocusSessionActive = true }));
        Assert.False(PauseSignalSource.SameSignals(a, a with { RemoteSession = true }));
    }

    [Fact]
    [Trait("Category", "Desktop")]
    public void Start_ReadsTheSignalsOfThisMachine()
    {
        var displays = new StaticDisplays([Main, Left]);
        var log = new RecordingLog();
        using var source = new PauseSignalSource(displays, log);
        Assert.Same(PauseSignals.None, source.Current);

        source.Start();
        source.Start();

        // Whatever this machine is doing (locked, presenting, gaming), the snapshot is well formed.
        PauseSignals current = source.Current;
        Assert.True(Enum.IsDefined(current.NotificationState));
        Assert.All(current.FullScreenDisplayIds, id => Assert.Contains(id, new[] { "main", "left" }));
        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
    }

    [Fact]
    public void Dispose_StopsWithoutRaising()
    {
        var source = new PauseSignalSource(new StaticDisplays([Main]), new RecordingLog());
        source.Start();
        int raised = 0;
        source.Changed += (_, _) => raised++;

        source.Dispose();
        source.Dispose();
        Thread.Sleep(1500);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Dispose_BeforeStartIsFine()
    {
        var source = new PauseSignalSource(new StaticDisplays([Main]), new RecordingLog());

        source.Dispose();
        source.Start();

        Assert.Same(PauseSignals.None, source.Current);
    }

    private sealed class FakeWindows : IWindowProbe
    {
        private readonly Dictionary<nint, WindowFacts> windows = [];

        public nint Foreground { get; set; }

        public void Show(nint window, RectI bounds, bool foreground = false, bool own = false)
        {
            windows[window] = new WindowFacts(bounds, own);
            if (foreground)
            {
                Foreground = window;
            }
        }

        public void Hide(nint window) => windows.Remove(window);

        public WindowFacts? Inspect(nint window) => windows.TryGetValue(window, out WindowFacts facts) ? facts : null;
    }

    private sealed class StaticDisplays(IReadOnlyList<DisplayInfo> displays) : IDisplayService
    {
        public event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DisplayInfo> Displays => displays;

        public DisplayInfo Primary => displays[0];

        public DisplayInfo FromPoint(PointI point) => DisplayTopology.Nearest(displays, point);

        public DisplayInfo FromCursor() => displays[0];

        public void Dispose()
        {
        }
    }
}
