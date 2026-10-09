using System.Diagnostics;
using HolidayLights.Rendering.Engine;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Layers;
using HolidayLights.Rendering.Shell;

namespace HolidayLights.Tests.Rendering;

public sealed class LayerPolicyTests
{
    [Fact]
    public void FallbackChain_FollowsTheSpecOrder()
    {
        Assert.Equal([LayerMode.BehindIcons, LayerMode.InFrontOfIcons, LayerMode.OnTop], LayerModeChain.For(LayerMode.BehindIcons));
        Assert.Equal([LayerMode.InFrontOfIcons, LayerMode.OnTop], LayerModeChain.For(LayerMode.InFrontOfIcons));
        Assert.Equal([LayerMode.OnTop], LayerModeChain.For(LayerMode.OnTop));
    }

    [Fact]
    public void WithoutAShell_OnlyTheRequestedModeIsTriedUntilTheBackoffAllowsMore()
    {
        Assert.Equal([LayerMode.BehindIcons], LayerModeChain.Candidates(LayerMode.BehindIcons, shellRunning: false, allowFallbackWithoutShell: false));
        Assert.Equal(LayerModeChain.For(LayerMode.BehindIcons), LayerModeChain.Candidates(LayerMode.BehindIcons, shellRunning: false, allowFallbackWithoutShell: true));
        Assert.Equal(LayerModeChain.For(LayerMode.InFrontOfIcons), LayerModeChain.Candidates(LayerMode.InFrontOfIcons, shellRunning: true, allowFallbackWithoutShell: false));
        Assert.Equal([LayerMode.OnTop], LayerModeChain.Candidates(LayerMode.OnTop, shellRunning: false, allowFallbackWithoutShell: false));
    }

    [Fact]
    public void IsBetter_ComparesPositionsInTheChain()
    {
        Assert.True(LayerModeChain.IsBetter(LayerMode.BehindIcons, LayerMode.InFrontOfIcons, LayerMode.BehindIcons));
        Assert.True(LayerModeChain.IsBetter(LayerMode.InFrontOfIcons, LayerMode.OnTop, LayerMode.BehindIcons));
        Assert.False(LayerModeChain.IsBetter(LayerMode.OnTop, LayerMode.InFrontOfIcons, LayerMode.BehindIcons));
        Assert.False(LayerModeChain.IsBetter(LayerMode.BehindIcons, LayerMode.OnTop, LayerMode.OnTop));
    }

    [Fact]
    public void DeviceRecovery_SwitchesToWarpAfterTwoLossesWithinAMinute()
    {
        var policy = new DeviceRecoveryPolicy(forceSoftware: false);
        long t = 1_000_000;
        Assert.False(policy.RecordLoss(t));
        Assert.False(policy.UseSoftware);
        Assert.False(policy.RecordLoss(t + 61 * Stopwatch.Frequency));
        Assert.False(policy.UseSoftware);
        Assert.True(policy.RecordLoss(t + 90 * Stopwatch.Frequency));
        Assert.True(policy.UseSoftware);
        Assert.False(policy.RecordLoss(t + 91 * Stopwatch.Frequency));
        Assert.Equal(4, policy.LossCount);
        Assert.True(new DeviceRecoveryPolicy(forceSoftware: true).UseSoftware);
    }

    [Fact]
    public void ThreadRestarts_HappenAtMostOncePerMinute()
    {
        var policy = new RestartPolicy();
        long t = 5_000_000;
        Assert.Equal(TimeSpan.Zero, policy.DelayBeforeRestart(t));
        policy.RecordRestart(t);
        Assert.InRange(policy.DelayBeforeRestart(t + 10 * Stopwatch.Frequency).TotalSeconds, 49.9, 50.1);
        Assert.Equal(TimeSpan.Zero, policy.DelayBeforeRestart(t + 61 * Stopwatch.Frequency));
    }

    [Fact]
    public void ExplorerBackoff_DoublesUpToThirtySeconds()
    {
        var backoff = new Backoff();
        double[] delays = [.. Enumerable.Range(0, 9).Select(_ => backoff.Next().TotalMilliseconds)];

        Assert.Equal([500, 1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000, 30_000], delays);
        Assert.Equal(9, backoff.Attempts);
        backoff.Reset();
        Assert.Equal(500, backoff.Next().TotalMilliseconds);
    }

    [Fact]
    public void OnTop_SitsDirectlyBelowEveryTaskbar()
    {
        nint layer = 50;
        var snapshot = new ZOrderSnapshot(
        [
            new ZOrderEntry(10, Topmost: true, IsTaskbar: false, IsOurs: false),
            new ZOrderEntry(20, Topmost: true, IsTaskbar: true, IsOurs: false),
            new ZOrderEntry(layer, Topmost: true, IsTaskbar: false, IsOurs: true),
            new ZOrderEntry(30, Topmost: true, IsTaskbar: true, IsOurs: false),
            new ZOrderEntry(40, Topmost: false, IsTaskbar: false, IsOurs: false),
        ]);

        Assert.True(snapshot.OnTopNeedsFix(layer));
        Assert.Equal(30, snapshot.OnTopInsertAfter());

        var fixedOrder = new ZOrderSnapshot(
        [
            new ZOrderEntry(20, true, true, false),
            new ZOrderEntry(30, true, true, false),
            new ZOrderEntry(layer, true, false, true),
            new ZOrderEntry(40, false, false, false),
        ]);
        Assert.False(fixedOrder.OnTopNeedsFix(layer));
        Assert.True(new ZOrderSnapshot([new ZOrderEntry(layer, false, false, true)]).OnTopNeedsFix(layer));
        Assert.Equal(WindowPositions.TopMost, new ZOrderSnapshot([new ZOrderEntry(layer, true, false, true)]).OnTopInsertAfter());
    }

    [Fact]
    public void InFrontOfIcons_StaysDirectlyAboveProgman()
    {
        nint progman = 100;
        nint first = 1;
        nint second = 2;
        var inPlace = new ZOrderSnapshot(
        [
            new ZOrderEntry(7, false, false, false),
            new ZOrderEntry(first, false, false, true),
            new ZOrderEntry(second, false, false, true),
            new ZOrderEntry(8, false, false, false, Visible: false),
            new ZOrderEntry(progman, false, false, false),
        ]);
        Assert.False(inPlace.InFrontOfIconsNeedsFix(first, progman));
        Assert.False(inPlace.InFrontOfIconsNeedsFix(second, progman));

        var coveredBelow = new ZOrderSnapshot(
        [
            new ZOrderEntry(first, false, false, true),
            new ZOrderEntry(9, false, false, false),
            new ZOrderEntry(progman, false, false, false),
        ]);
        Assert.True(coveredBelow.InFrontOfIconsNeedsFix(first, progman));

        var belowDesktop = new ZOrderSnapshot([new ZOrderEntry(progman, false, false, false), new ZOrderEntry(first, false, false, true)]);
        Assert.True(belowDesktop.InFrontOfIconsNeedsFix(first, progman));
        Assert.True(belowDesktop.IsBelow(first, progman));
        Assert.False(inPlace.IsBelow(first, progman));
    }

    [Fact]
    public void BottomOfTopmostBand_IsTheLastTopmostWindowThatIsNotOurs()
    {
        var snapshot = new ZOrderSnapshot(
        [
            new ZOrderEntry(1, true, false, false),
            new ZOrderEntry(2, true, true, false),
            new ZOrderEntry(3, true, false, true),
            new ZOrderEntry(4, false, false, false),
        ]);

        Assert.Equal(2, snapshot.BottomOfTopmostBand());
        Assert.Equal(0, new ZOrderSnapshot([new ZOrderEntry(4, false, false, false)]).BottomOfTopmostBand());
    }

    [Fact]
    public void OnTop_InsertedAfterATaskbarDemotedInTheMeantime_GoesBackIntoTheTopmostBandAtOnce()
    {
        // Explorer demotes the taskbar between the snapshot that picked it and the insert (integration issue #2).
        nint primaryTaskbar = 10;
        nint secondaryTaskbar = 20;
        nint app = 30;
        nint layer = 50;
        var order = new SimulatedZOrder((primaryTaskbar, true), (secondaryTaskbar, true), (layer, true), (app, false));
        var window = new SimulatedWindow(order, layer);
        nint picked = order.LowestTopmostTaskbar(primaryTaskbar, secondaryTaskbar);
        Assert.Equal(secondaryTaskbar, picked);
        order.Demote(secondaryTaskbar);

        window.PlaceAfter(picked);
        Assert.False(window.IsTopmost);
        Assert.True(order.IndexOf(layer) > order.IndexOf(app), "The stale insert should have dropped the layer below the app window.");

        TopmostBand.Keep(window, () => order.LowestTopmostTaskbar(primaryTaskbar, secondaryTaskbar));

        Assert.True(window.IsTopmost);
        Assert.Equal(order.IndexOf(primaryTaskbar) + 1, order.IndexOf(layer));
        Assert.True(order.IndexOf(layer) < order.IndexOf(app));
    }

    [Fact]
    public void TopmostBand_StaysAtTheTopOfTheBandWhenTheAnchorKeepsLosingTopmost()
    {
        nint taskbar = 10;
        nint layer = 50;
        var order = new SimulatedZOrder((taskbar, false), (layer, false));
        var window = new SimulatedWindow(order, layer);
        int picks = 0;

        TopmostBand.Keep(window, () =>
        {
            // Every pick is stale: the taskbar is topmost when picked and demoted before the insert.
            picks++;
            order.Promote(taskbar);
            order.Demote(taskbar);
            return taskbar;
        });

        Assert.True(window.IsTopmost);
        Assert.Equal(0, order.IndexOf(layer));
        Assert.Equal(2, picks);
    }

    [Fact]
    public void TopmostBand_LeavesATopmostWindowAlone()
    {
        nint taskbar = 10;
        nint layer = 50;
        var order = new SimulatedZOrder((taskbar, true), (layer, true));
        var window = new SimulatedWindow(order, layer);

        TopmostBand.Keep(window, () => throw new InvalidOperationException("No snapshot is needed when the window is topmost."));

        Assert.Equal(0, order.Placements);
        Assert.Equal(1, order.IndexOf(layer));
    }

    [Fact]
    public void TopLevelLayerWindows_AreNotTakenForFullScreenApps()
    {
        // Explorer's full-screen detection ignores windows with NonRudeHWND, so a layer covering its monitor neither makes
        // Windows report QUNS_BUSY nor drops the taskbars out of the topmost band. The window is never shown.
        WindowClasses.EnsureRegistered();
        nint hwnd = LayerWindow.CreateTopLevel(ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate, RectI.FromXYWH(0, 0, 1, 1), owner: 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            Assert.Equal(1, User32.GetPropW(hwnd, LayerWindow.NonRudeProperty));
            Assert.False(User32.IsWindowVisible(hwnd));
        }
        finally
        {
            User32.DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void BehindIcons_LiesBetweenTheIconsAndTheWallpaper()
    {
        nint defView = 10;
        nint worker = 30;
        Assert.True(BehindIconsOrder.IsInPlace([defView, 21, 22, worker], defView, worker, 22));
        Assert.False(BehindIconsOrder.IsInPlace([21, defView, worker], defView, worker, 21));
        Assert.False(BehindIconsOrder.IsInPlace([defView, worker, 21], defView, worker, 21));
        Assert.True(BehindIconsOrder.IsInPlace([defView, 21], defView, 0, 21));
        Assert.False(BehindIconsOrder.IsInPlace([21], defView, worker, 21));
    }

    /// <summary>
    /// A top-level z-order (topmost first) with Win32's rules for <c>SetWindowPos</c>: <c>HWND_TOPMOST</c> puts a window at the
    /// top and makes it topmost; inserted after a window it takes that window's topmost state (a topmost window inserted after
    /// a non-topmost one loses <c>WS_EX_TOPMOST</c>).
    /// </summary>
    private sealed class SimulatedZOrder
    {
        private readonly List<(nint Hwnd, bool Topmost)> windows;

        public SimulatedZOrder(params (nint Hwnd, bool Topmost)[] windows) => this.windows = [.. windows];

        public int Placements { get; private set; }

        public int IndexOf(nint hwnd) => windows.FindIndex(w => w.Hwnd == hwnd);

        public bool IsTopmost(nint hwnd) => windows[IndexOf(hwnd)].Topmost;

        public void PlaceAfter(nint hwnd, nint insertAfter)
        {
            Placements++;
            windows.RemoveAt(IndexOf(hwnd));
            if (insertAfter == WindowPositions.TopMost)
            {
                windows.Insert(0, (hwnd, true));
                return;
            }

            int after = IndexOf(insertAfter);
            windows.Insert(after + 1, (hwnd, windows[after].Topmost));
        }

        /// <summary>Explorer demotes a taskbar: it leaves the topmost band for the bottom of the z-order (as observed live).</summary>
        public void Demote(nint hwnd)
        {
            windows.RemoveAt(IndexOf(hwnd));
            windows.Add((hwnd, false));
        }

        public void Promote(nint hwnd)
        {
            windows.RemoveAt(IndexOf(hwnd));
            windows.Insert(0, (hwnd, true));
        }

        /// <summary>What <see cref="ZOrderSnapshot.OnTopInsertAfter"/> picks: the lowest taskbar that is topmost now.</summary>
        public nint LowestTopmostTaskbar(params nint[] taskbars) =>
            new ZOrderSnapshot([.. windows.Select(w => new ZOrderEntry(w.Hwnd, w.Topmost, w.Topmost && taskbars.Contains(w.Hwnd), IsOurs: false))]).OnTopInsertAfter();
    }

    private sealed class SimulatedWindow(SimulatedZOrder order, nint hwnd) : IBandWindow
    {
        public bool IsTopmost => order.IsTopmost(hwnd);

        public void PlaceAfter(nint insertAfter, bool show = false) => order.PlaceAfter(hwnd, insertAfter);
    }
}
