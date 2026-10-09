using System.Diagnostics;
using System.Runtime.InteropServices;
using HolidayLights.Rendering;
using HolidayLights.Rendering.Interop;
using HolidayLights.Tests.Rendering.Fakes;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live check of Bulb Drawing changes in quick succession and of how Windows sees the top-level layers (PRODUCT-SPEC 4.4,
/// 5.1; runs only with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>). The bulbs are fully transparent (the 16-pixel spacer), so nothing
/// shows on the desktop: real layer windows in every mode, nothing in Windows changed.
/// <list type="bullet">
/// <item>Two or three changes inside the 150 ms move fade end with every display shown in the mode requested last (review
/// finding: the displays that were mid-move stayed dark for the rest of the session).</item>
/// <item>On-top layers cover their monitors without being taken for a full-screen app: no <c>QUNS_BUSY</c>, the taskbars stay
/// topmost and above the layers.</item>
/// </list>
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed partial class LiveLayerMoveProbe : IClassFixture<RealLights>
{
    private const int QunsBusy = 2;

    private readonly RealLights lights;
    private readonly ITestOutputHelper output;

    public LiveLayerMoveProbe(RealLights lights, ITestOutputHelper output)
    {
        this.lights = lights;
        this.output = output;
    }

    [LiveFact]
    public async Task QuickChangesEndInTheLastModeAndOnTopIsNotAFullScreenApp()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        var recipe = new SceneRecipe { Arrangement = SlotAssignment.Empty with { Top = [BulbIds.BuiltIn("16-pixel-spacer")] } };
        LightsScene scene = lights.Scene(recipe, displays, LayerMode.BehindIcons);
        Assert.NotEmpty(scene.Layout.Placements);
        int quns = NotificationState();
        Dictionary<nint, bool> taskbars = Taskbars();
        output.WriteLine($"Before: QUNS {quns}, taskbars {Describe(taskbars)}.");

        var log = new RecordingLog();
        using var presenter = new LightsPresenter(lights.Catalog, lights.Sprites, lights.Flash, new FakeMusicEventSource(), new LightsPresenterOptions(), log);
        long revision = 1;
        LightsScene Next(LayerMode mode) => scene with { Revision = ++revision, RequestedLayer = mode };
        bool Shown(LayerMode mode) => LiveSupport.AllShown(presenter, displays.Count, mode);
        try
        {
            presenter.Start();
            presenter.Apply(scene, SceneTransition.None);
            Assert.True(LiveSupport.WaitFor(() => Shown(LayerMode.BehindIcons), TimeSpan.FromSeconds(10)), "The lights did not start.");

            // Two changes 40 ms apart, inside the 150 ms fade of the first.
            var watch = Stopwatch.StartNew();
            presenter.Apply(Next(LayerMode.OnTop), SceneTransition.Automatic);
            Thread.Sleep(40);
            presenter.Apply(Next(LayerMode.BehindIcons), SceneTransition.Automatic);
            Assert.True(
                LiveSupport.WaitFor(() => Shown(LayerMode.BehindIcons), TimeSpan.FromSeconds(3)),
                $"Two quick changes left displays without lights: {LiveSupport.Describe(presenter.Status)}");
            output.WriteLine($"Two changes {watch.ElapsedMilliseconds} ms: every display behind the icons again.");
            await Task.Delay(400);

            // Three changes, the last one late in the fade, ending on top.
            presenter.Apply(Next(LayerMode.OnTop), SceneTransition.Automatic);
            Thread.Sleep(30);
            presenter.Apply(Next(LayerMode.InFrontOfIcons), SceneTransition.Automatic);
            Thread.Sleep(90);
            presenter.Apply(Next(LayerMode.OnTop), SceneTransition.Automatic);
            Assert.True(
                LiveSupport.WaitFor(() => Shown(LayerMode.OnTop), TimeSpan.FromSeconds(3)),
                $"Three quick changes left displays without lights: {LiveSupport.Describe(presenter.Status)}");

            // A quick change followed by one without a transition (Undo with reduced motion): the last one wins at once.
            presenter.Apply(Next(LayerMode.InFrontOfIcons), SceneTransition.Automatic);
            Thread.Sleep(40);
            presenter.Apply(Next(LayerMode.OnTop), SceneTransition.None);
            Assert.True(
                LiveSupport.WaitFor(() => Shown(LayerMode.OnTop), TimeSpan.FromSeconds(3)),
                $"A change without a transition during a move left displays without lights: {LiveSupport.Describe(presenter.Status)}");

            // On top, the layers cover their monitors: Explorer must not take them for a full-screen app.
            Thread.Sleep(2_000);
            LiveSupport.CheckWindows(await presenter.GetDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(2)), LayerMode.OnTop, displays.Count, output);
            foreach (LayerDiagnostics layer in presenter.Diagnostics.Layers)
            {
                Assert.Equal(1, User32.GetPropW((nint)layer.Window, "NonRudeHWND"));
            }

            int qunsOnTop = NotificationState();
            Dictionary<nint, bool> taskbarsOnTop = Taskbars();
            output.WriteLine($"On top: QUNS {qunsOnTop}, taskbars {Describe(taskbarsOnTop)}.");
            if (quns != QunsBusy)
            {
                Assert.NotEqual(QunsBusy, qunsOnTop);
            }

            foreach (nint taskbar in taskbars.Where(t => t.Value).Select(t => t.Key))
            {
                Assert.True(taskbarsOnTop.GetValueOrDefault(taskbar), $"Taskbar 0x{taskbar:X} left the topmost band while the lights were on top.");
            }
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: false).WaitAsync(TimeSpan.FromSeconds(5));
        }

        LiveSupport.WriteLog(log, output);
        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
        Assert.Equal(0, LiveSupport.CountLayerWindows());
    }

    private static int NotificationState() => SHQueryUserNotificationState(out int state) == 0 ? state : -1;

    /// <summary>Every taskbar (primary and secondary) with its topmost state.</summary>
    private static Dictionary<nint, bool> Taskbars()
    {
        var found = new Dictionary<nint, bool>();
        foreach (string name in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            for (nint hwnd = User32.FindWindowExW(0, 0, name, null); hwnd != 0; hwnd = User32.FindWindowExW(0, hwnd, name, null))
            {
                found[hwnd] = (User32.ExtendedStyleOf(hwnd) & ExtendedWindowStyles.Topmost) != 0;
            }
        }

        return found;
    }

    private static string Describe(Dictionary<nint, bool> taskbars) =>
        string.Join(", ", taskbars.Select(t => $"0x{t.Key:X} {(t.Value ? "topmost" : "NOT topmost")}"));

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
