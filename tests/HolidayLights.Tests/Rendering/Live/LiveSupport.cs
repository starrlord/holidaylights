using System.Diagnostics;
using HolidayLights.Audio.Events;
using HolidayLights.Rendering;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Shell;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>What the live probes share: the presenter on the real Core, waiting, window checks and the output folder.</summary>
internal static class LiveSupport
{
    /// <summary>
    /// Where captures go: <c>HOLIDAYLIGHTS_PROBE_OUT</c>, else <c>%TEMP%\HolidayLightsProbe</c>, in a sub-folder per probe.
    /// </summary>
    public static string OutputFolder(string probe) => Path.Combine(
        Environment.GetEnvironmentVariable("HOLIDAYLIGHTS_PROBE_OUT") is { Length: > 0 } folder ? folder : Path.Combine(Path.GetTempPath(), "HolidayLightsProbe"),
        probe);

    /// <summary>A presenter on the real catalog, sprite provider and flash engine, reading music from <paramref name="music"/>.</summary>
    public static LightsPresenter CreatePresenter(RealLights lights, MusicEventHub music, RecordingLog log, bool software = false) =>
        new(lights.Catalog, lights.Sprites, lights.Flash, music, new LightsPresenterOptions { ForceSoftwareRendering = software }, log);

    /// <summary>Polls a condition every 20 ms.</summary>
    public static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    /// <summary>True when every display shows its lights in <paramref name="mode"/> (status and windows).</summary>
    public static bool AllShown(LightsPresenter presenter, int displays, LayerMode mode) =>
        presenter.Status.Displays.Count == displays && presenter.Status.Displays.All(d => d.Effective == mode)
        && presenter.Diagnostics.Layers.Count == displays && presenter.Diagnostics.Layers.All(l => l.Visible && l.Mode == mode);

    /// <summary>The status in one line.</summary>
    public static string Describe(LightsStatus status) =>
        $"{status.Health}, requested {status.Requested}, "
        + string.Join(", ", status.Displays.Select(d => $"{d.DisplayId}={d.Effective?.ToString() ?? "none"}{(d.Resting ? " (resting)" : string.Empty)}"));

    /// <summary>
    /// Checks the windows of every layer against PRODUCT-SPEC 5.1: visible, exactly the display, and the z-order rule of the
    /// mode (behind the icons: a child of Progman between <c>SHELLDLL_DefView</c> and the wallpaper; in front: owned by
    /// Progman, not topmost, click-through; on top: topmost below every taskbar).
    /// </summary>
    public static void CheckWindows(LightsDiagnostics diagnostics, LayerMode mode, int displays, ITestOutputHelper output)
    {
        Assert.Equal(displays, diagnostics.Layers.Count);
        nint progman = TestWindows.FindWindowW("Progman", null);
        foreach (LayerDiagnostics layer in diagnostics.Layers)
        {
            nint hwnd = (nint)layer.Window;
            Assert.True(User32.IsWindow(hwnd) && User32.IsWindowVisible(hwnd), $"{layer.DisplayId}: the layer window is not visible.");
            Assert.True(User32.GetWindowRect(hwnd, out RECT rect));
            Assert.Equal(layer.Bounds, rect.ToRectI());
            uint exStyle = User32.ExtendedStyleOf(hwnd);
            switch (mode)
            {
                case LayerMode.BehindIcons:
                    nint defView = User32.FindWindowExW(progman, 0, "SHELLDLL_DefView", null);
                    nint worker = User32.FindWindowExW(progman, 0, "WorkerW", null);
                    Assert.Equal(progman, TestWindows.GetAncestor(hwnd, TestWindows.ParentAncestor));
                    Assert.True(BehindIconsOrder.IsInPlace(Win32ShellWindows.ChildrenOf(progman), defView, worker, hwnd), "The layer is not between the icons and the wallpaper.");
                    break;
                case LayerMode.InFrontOfIcons:
                    Assert.Equal(progman, User32.GetWindow(hwnd, TestWindows.OwnerWindow));
                    Assert.Equal(0u, exStyle & ExtendedWindowStyles.Topmost);
                    Assert.NotEqual(0u, exStyle & ExtendedWindowStyles.Transparent);
                    break;
                default:
                    Assert.NotEqual(0u, exStyle & ExtendedWindowStyles.Topmost);
                    Assert.NotEqual(0u, exStyle & ExtendedWindowStyles.Transparent);
                    Assert.False(Win32ShellWindows.Instance.Snapshot(new HashSet<nint> { hwnd }).OnTopNeedsFix(hwnd), "A taskbar is below the on-top layer.");
                    break;
            }

            output.WriteLine($"  {mode}: display {layer.DisplayId} window 0x{layer.Window:X} {layer.Bounds} ex 0x{exStyle:X8}, {layer.Bulbs} bulbs");
        }
    }

    /// <summary>Counts the presenter's windows still alive (top-level and children of Progman).</summary>
    public static int CountLayerWindows()
    {
        int count = 0;
        for (nint hwnd = User32.FindWindowExW(0, 0, "HolidayLights.BulbLayer", null); hwnd != 0; hwnd = User32.FindWindowExW(0, hwnd, "HolidayLights.BulbLayer", null))
        {
            count++;
        }

        nint progman = TestWindows.FindWindowW("Progman", null);
        for (nint hwnd = User32.FindWindowExW(progman, 0, "HolidayLights.BulbLayer", null); hwnd != 0; hwnd = User32.FindWindowExW(progman, hwnd, "HolidayLights.BulbLayer", null))
        {
            count++;
        }

        return count;
    }

    /// <summary>Writes the log entries to the test output.</summary>
    public static void WriteLog(RecordingLog log, ITestOutputHelper output)
    {
        foreach ((AppLogLevel level, string source, string message, Exception? exception) in log.Entries)
        {
            output.WriteLine($"{level} {source}: {message}{(exception is null ? string.Empty : " - " + exception.Message)}");
        }
    }

    /// <summary>True when the window a click at a point would reach is one of the presenter's layers.</summary>
    public static bool IsLayerAt(int x, int y) =>
        User32.ClassNameOf(TestWindows.GetAncestor(TestWindows.WindowFromPoint(new POINT(x, y)), TestWindows.RootAncestor)) == "HolidayLights.BulbLayer"
        || User32.ClassNameOf(TestWindows.WindowFromPoint(new POINT(x, y))) == "HolidayLights.BulbLayer";

    /// <summary>True when an application window (not the desktop, not our layers) is the topmost window at a point.</summary>
    public static bool IsCoveredByWindow(int x, int y)
    {
        nint hwnd = TestWindows.WindowFromPoint(new POINT(x, y));
        string rootClass = User32.ClassNameOf(TestWindows.GetAncestor(hwnd, TestWindows.RootAncestor));
        return rootClass is not ("Progman" or "WorkerW" or "HolidayLights.BulbLayer");
    }
}

/// <summary>Live desktop tests never run in parallel with each other.</summary>
[CollectionDefinition(nameof(LiveDesktopCollection), DisableParallelization = true)]
public sealed class LiveDesktopCollection;
