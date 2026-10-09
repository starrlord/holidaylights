using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Layers;
using HolidayLights.Rendering.Presentation;
using HolidayLights.Rendering.Shell;
using HolidayLights.Rendering.Threading;

namespace HolidayLights.Rendering.Engine;

/// <summary>
/// Layer windows (PRODUCT-SPEC 5.1, 5.13): desktop host discovery with <c>0x052C</c>, the fallback chain, the z-order rules of
/// each mode, the 2 s maintenance, Explorer restarts with back-off, retries of the requested mode, Win+D and virtual desktops.
/// </summary>
internal sealed partial class LightsEngine
{
    private static readonly long SpawnWait = Ticks(1_000);
    private static readonly long PreferredRetryInterval = Ticks(30_000);

    private readonly Backoff explorerBackoff = new();
    private DesktopHosts hosts = DesktopHosts.None;
    private IReadOnlyList<MonitorState> topology = [];
    private VirtualDesktopManager? desktops;
    private nint watcher;
    private nint foregroundHook;
    private bool spawnSent;
    private long? spawnDeadline;
    private long? explorerRetryAt;
    private long? preferredRetryAt;
    private bool desktopRaised;

    private LayerMode Requested => Scene?.RequestedLayer ?? LayerMode.BehindIcons;

    /// <summary>
    /// The 2 s check is wanted while a layer window exists or Explorer is being waited for, except while a global rest hides
    /// every layer (locked, display off, away, Remote Desktop, our own screen saver): nobody sees the desktop then, so only
    /// the pause poll wakes the process (PRODUCT-SPEC 5.12.2, 5.14 "one 2 s poll"; review r1 #77). Unlock, display on and
    /// resume check everything at once (<see cref="SetPause"/>), and the check is due at once when a layer shows again.
    /// </summary>
    private bool MaintenanceWanted =>
        !RestingEverywhere && (layers.Values.Any(l => l.Window is not null) || explorerRetryAt is not null);

    private bool RestingEverywhere =>
        (pause.Global & (PauseReasons.SessionLocked | PauseReasons.DisplayOff | PauseReasons.UserNotPresent | PauseReasons.OwnScreenSaver | PauseReasons.RemoteSession)) != 0
        && !AnyLayerVisible;

    private long? ShellDeadline => Earliest(Earliest(spawnDeadline, explorerRetryAt), preferredRetryAt);

    /// <summary>Command: "Try Again" - the requested mode is tried at once for every layer in a fallback mode.</summary>
    public void RetryPreferredLayer() => RetryPreferred(Now, 0);

    /// <summary>Retries the requested mode (Try Again, every 30 s while degraded, after unlock, resume and display changes).</summary>
    private void RetryPreferred(long now, double fadeMilliseconds)
    {
        preferredRetryAt = null;
        spawnSent = false;
        RefreshHosts();
        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is { } window && window.Mode != Requested && layer.IsVisible)
            {
                TryUpgrade(layer, now);
            }
        }

        UpdateVisibility(now, fadeMilliseconds, applyState: true);
        ScheduleDegradedRetry(now);
    }

    /// <inheritdoc />
    public void OnShellEvent(ShellEvent shellEvent)
    {
        long now = Now;
        switch (shellEvent)
        {
            case ShellEvent.TaskbarCreated:
                // Explorer (re)started, or a DPI change: check the hosts, and try the requested mode again (PRODUCT-SPEC 5.1).
                Log.Info(LogSource, "TaskbarCreated: checking the desktop hosts.");
                Maintain(now, topologyEvent: true);
                if (explorerRetryAt is null)
                {
                    RetryPreferred(now, 0);
                }

                break;
            case ShellEvent.Resumed:
                // After sleep: revalidate the device (Maintain checks it), the hosts and the layers, then the requested mode.
                Maintain(now, topologyEvent: true);
                RetryPreferred(now, 0);
                break;
            default:
                topologyCheckAt ??= now + TopologyDebounce;
                break;
        }
    }

    /// <inheritdoc />
    public void OnWindowDestroyed(nint hwnd)
    {
        if (hwnd == watcher)
        {
            watcher = 0;
            return;
        }

        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is { } window && window.Hwnd == hwnd)
            {
                window.MarkDestroyed();
                topologyCheckAt ??= Now;
            }
        }
    }

    /// <inheritdoc />
    public void OnWindowDpiChanged(nint hwnd)
    {
        // A layer covers exactly one display: keep its rectangle instead of Windows' suggestion.
        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is { } window && window.Hwnd == hwnd)
            {
                window.EnsureBounds();
            }
        }
    }

    /// <inheritdoc />
    public void OnWorkerSpawned()
    {
        spawnDeadline = null;
        RefreshHosts();
        UpdateVisibility(Now, 0, applyState: true);
    }

    /// <inheritdoc />
    public void OnForegroundChanged(nint hwnd)
    {
        List<LayerWindow> inFront = [.. layers.Values.Select(l => l.Window).OfType<LayerWindow>().Where(w => w.Mode == LayerMode.InFrontOfIcons && w.IsShown)];
        if (inFront.Count == 0 || hosts.Progman == 0)
        {
            desktopRaised = false;
            return;
        }

        bool desktop = hwnd != 0 && (hwnd == hosts.Progman || User32.ClassNameOf(hwnd) is "WorkerW" or "Progman");
        if (desktop)
        {
            // Win+D brought the desktop host forward: keep the in-front layers visible at the bottom of the topmost band,
            // below the taskbars, until another window is activated (Rainmeter's technique).
            ZOrderSnapshot snapshot = Snapshot();
            foreach (LayerWindow window in inFront.Where(w => snapshot.IsBelow(w.Hwnd, hosts.Progman)))
            {
                window.PlaceAfter(WindowPositions.TopMost);
                nint after = Snapshot().BottomOfTopmostBand();
                if (after != 0)
                {
                    window.PlaceAfter(after);
                    TopmostBand.Keep(window, () => Snapshot().BottomOfTopmostBand());
                }

                desktopRaised = true;
            }
        }
        else if (desktopRaised)
        {
            foreach (LayerWindow window in inFront)
            {
                window.PlaceAfter(WindowPositions.NoTopMost);
                window.PlaceAfter(WindowPositions.Bottom);
            }

            desktopRaised = false;
        }
    }

    /// <summary>Gives a layer a window, trying the requested mode first. False while the shell is not ready or nothing works.</summary>
    private bool EnsureWindow(DisplayLayer layer, long now)
    {
        if (layer.Window is { IsAlive: true })
        {
            return true;
        }

        layer.Detach()?.Dispose();
        if (device is null || !ShellReady(now))
        {
            return false;
        }

        bool allowFallbackWithoutShell = explorerRetryAt is null || explorerBackoff.Attempts >= 3;
        foreach (LayerMode mode in LayerModeChain.Candidates(Requested, hosts.Progman != 0, allowFallbackWithoutShell))
        {
            if (LayerWindow.TryCreate(mode, layer.Display.Bounds, hosts, device) is not { } window)
            {
                continue;
            }

            if (!layer.Attach(window))
            {
                Log.Warn(LogSource, $"Display {layer.Display.Number}: the {mode} window refused the lights' visual tree.");
                layer.Detach();
                window.Dispose();
                continue;
            }

            if (mode != Requested)
            {
                Log.Warn(LogSource, $"Display {layer.Display.Number}: {Requested} is not available, the lights are drawn {mode}.");
                ScheduleDegradedRetry(now);
            }

            return true;
        }

        Log.Warn(LogSource, $"Display {layer.Display.Number}: no layer window could be created.");
        ScheduleDegradedRetry(now);
        return false;
    }

    /// <summary>
    /// The wallpaper layer must exist for "behind the icons": when Progman has not split it yet, <c>0x052C</c> (wParam 0xD,
    /// lParam 1, never 0) is sent once per attempt without blocking, and window creation waits up to 1 s for Explorer.
    /// </summary>
    private unsafe bool ShellReady(long now)
    {
        if (Requested != LayerMode.BehindIcons || hosts.Layout != DesktopLayout.NotSplit || hosts.Progman == 0)
        {
            return true;
        }

        if (!spawnSent)
        {
            spawnSent = true;
            spawnDeadline = now + SpawnWait;
            User32.SendMessageCallbackW(hosts.Progman, WindowMessages.ProgmanSpawnWorker, 0xD, 0x1, &OnSpawnCompleted, 0);
            return false;
        }

        return spawnDeadline is not { } deadline || now >= deadline;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnSpawnCompleted(nint hwnd, uint message, nuint data, nint result) =>
        LightsThreadContext.Guard(sink => sink.OnWorkerSpawned());

    private void TickShell(long now)
    {
        if (spawnDeadline is { } deadline && now >= deadline)
        {
            OnWorkerSpawned();
        }

        if (explorerRetryAt is { } retry && now >= retry)
        {
            RecoverLayers(now);
        }

        if (preferredRetryAt is { } preferred && now >= preferred)
        {
            RetryPreferred(now, 0);
        }
    }

    private void RefreshHosts()
    {
        DesktopHosts found = DesktopHostLocator.Locate(Win32ShellWindows.Instance);
        if (found.Progman != hosts.Progman)
        {
            spawnSent = false;
        }

        if (found != hosts)
        {
            Log.Info(LogSource, $"Desktop hosts: {found.Layout}.");
        }

        hosts = found;
    }

    /// <summary>The 2 s check (PRODUCT-SPEC 5.1, 5.13): device, hosts, dead windows, bounds, z-order, virtual desktops, topology.</summary>
    private void Maintain(long now, bool topologyEvent)
    {
        nextMaintenance = now + MaintenanceInterval;
        PublishDiagnostics();
        ReleaseUnusedSurfaces();
        if (device is null)
        {
            return;
        }

        if (!device.IsValid())
        {
            OnDeviceLost(now, "the device state check failed");
            return;
        }

        if (!MaintenanceWanted && !topologyEvent)
        {
            return;
        }

        RefreshGlowScales(force: topologyEvent);
        DesktopHosts before = hosts;
        RefreshHosts();
        bool shellChanged = hosts.Progman != before.Progman || hosts.DefView != before.DefView || hosts.Layout != before.Layout;
        bool windowLost = layers.Values.Any(l => l.Window is { IsAlive: false });
        if ((shellChanged && before.Progman != 0) || windowLost)
        {
            BeginExplorerRecovery(now);
        }
        else if (shellChanged && explorerRetryAt is null)
        {
            // The desktop appeared (Explorer was not running): the requested mode is tried at once (PRODUCT-SPEC 5.1).
            RetryPreferred(now, 0);
        }

        CheckTopology(now, topologyEvent);
        ZOrderSnapshot? snapshot = null;
        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is not { IsAlive: true, IsShown: true } window)
            {
                continue;
            }

            dirty |= window.EnsureBounds();
            KeepInPlace(window, ref snapshot);
            if (window.Mode != LayerMode.BehindIcons && desktops is not null && !desktops.IsOnCurrentDesktop(window.Hwnd))
            {
                desktops.MoveToDesktopOf(window.Hwnd, User32.GetForegroundWindow());
            }
        }
    }

    /// <summary>Re-asserts the z-order rule of a window's mode when something moved it.</summary>
    private void KeepInPlace(LayerWindow window, ref ZOrderSnapshot? snapshot)
    {
        switch (window.Mode)
        {
            case LayerMode.BehindIcons when hosts.Layout == DesktopLayout.Raised:
                if (!BehindIconsOrder.IsInPlace(Win32ShellWindows.ChildrenOf(hosts.Progman), hosts.DefView, hosts.WallpaperWorker, window.Hwnd))
                {
                    window.PlaceAfter(hosts.DefView);
                }

                break;
            case LayerMode.InFrontOfIcons when !desktopRaised:
                snapshot ??= Snapshot();
                if (snapshot.InFrontOfIconsNeedsFix(window.Hwnd, hosts.Progman))
                {
                    window.PlaceAfter(WindowPositions.Bottom);
                    snapshot = null;
                }

                break;
            case LayerMode.OnTop:
                snapshot ??= Snapshot();
                if (snapshot.OnTopNeedsFix(window.Hwnd))
                {
                    window.PlaceAfter(snapshot.OnTopInsertAfter());
                    TopmostBand.Keep(window, OnTopAnchor);
                    snapshot = null;
                }

                break;
        }
    }

    /// <summary>Where a window is inserted when shown.</summary>
    private nint InsertAfter(LayerWindow window) => window.Mode switch
    {
        LayerMode.BehindIcons => hosts.Layout == DesktopLayout.Raised ? hosts.DefView : 0,
        LayerMode.InFrontOfIcons => WindowPositions.Bottom,
        _ => OnTopAnchor(),
    };

    /// <summary>Where an on-top window goes: below the taskbars that are topmost now.</summary>
    private nint OnTopAnchor() => Snapshot().OnTopInsertAfter();

    /// <summary>Shows a layer's window at the place of its mode with the layer opacity <paramref name="opacity"/>.</summary>
    private void ShowLayer(DisplayLayer layer, OpacityCurve opacity)
    {
        LayerWindow window = layer.Window!;
        bool wasShown = window.IsShown;
        layer.Show(InsertAfter(window), opacity, batch!);
        if (!wasShown && window.Mode == LayerMode.OnTop)
        {
            TopmostBand.Keep(window, OnTopAnchor);
        }
    }

    private ZOrderSnapshot Snapshot()
    {
        var ours = new HashSet<nint>();
        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is { } window)
            {
                ours.Add(window.Hwnd);
            }
        }

        return Win32ShellWindows.Instance.Snapshot(ours);
    }

    /// <summary>Notices monitors that vanished or moved before the next scene arrives and hides their layers until then.</summary>
    private void CheckTopology(long now, bool topologyEvent)
    {
        IReadOnlyList<MonitorState> current = MonitorTopology.Capture();
        TopologyChange change = TopologyChange.Between(topology, current);
        if (change.IsEmpty && !topologyEvent)
        {
            return;
        }

        topology = current;
        bool staleChanged = RefreshStaleDisplays();
        if (!change.IsEmpty)
        {
            // After a display change the requested mode is tried again at once (PRODUCT-SPEC 5.1).
            Log.Info(LogSource, $"Display topology changed: {change.Describe()}.");
            RetryPreferred(now, 0);
        }
        else if (staleChanged)
        {
            UpdateVisibility(now, 0, applyState: true);
        }
    }

    /// <summary>Layers whose display rectangle no monitor shows any more stay hidden (the scene is stale until the next one).</summary>
    /// <returns>True when the set changed.</returns>
    private bool RefreshStaleDisplays()
    {
        HashSet<string> stale = TopologyChange.StaleDisplays(layers.Values.Select(l => (l.DisplayId, l.Display.Bounds)), topology);
        if (stale.SetEquals(staleDisplays))
        {
            return false;
        }

        staleDisplays.Clear();
        staleDisplays.UnionWith(stale);
        return true;
    }

    /// <summary>Explorer restarted (or the desktop was rebuilt): re-create the layers with back-off, the requested mode first.</summary>
    private void BeginExplorerRecovery(long now)
    {
        if (explorerRetryAt is not null)
        {
            return;
        }

        Log.Warn(LogSource, "The desktop hosts changed or a layer window was destroyed; re-creating the lights.");
        explorerBackoff.Reset();
        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is { } window && (!window.IsAlive || window.Mode != LayerMode.OnTop))
            {
                // A layer moving to another mode loses its window here: the recovery below shows it in the requested mode.
                movingLayers.Remove(layer.DisplayId);
                layer.Detach()?.Dispose();
            }
        }

        explorerRetryAt = now + Ticks(explorerBackoff.Next().TotalMilliseconds);
    }

    private void RecoverLayers(long now)
    {
        RefreshHosts();
        UpdateVisibility(now, 0, applyState: true);
        foreach (DisplayLayer layer in layers.Values)
        {
            if (layer.Window is { } window && window.Mode != Requested && layer.IsVisible)
            {
                TryUpgrade(layer, now);
            }
        }

        bool done = layers.Values.All(l => !WantsVisible(l) || l.Window is { IsAlive: true } window && window.Mode == Requested);
        if (done)
        {
            explorerRetryAt = null;
            explorerBackoff.Reset();
            Log.Info(LogSource, "The lights are back after the desktop changed.");
        }
        else
        {
            explorerRetryAt = now + Ticks(explorerBackoff.Next().TotalMilliseconds);
        }

        RebuildRoutes();
        dirty = true;
    }

    private bool WantsVisible(DisplayLayer layer) =>
        Scene is { LightsOn: true } && !pause.IsGloballyPaused && !pause.RestingDisplayIds.Contains(layer.DisplayId)
        && !staleDisplays.Contains(layer.DisplayId) && layer.Scene is { Bulbs.Count: > 0 };

    /// <summary>Moves a layer in a fallback mode to a better mode when that works now ("the lights move back silently").</summary>
    private void TryUpgrade(DisplayLayer layer, long now)
    {
        LayerWindow current = layer.Window!;
        foreach (LayerMode mode in LayerModeChain.For(Requested))
        {
            if (!LayerModeChain.IsBetter(mode, current.Mode, Requested) || !ShellReady(now))
            {
                continue;
            }

            if (LayerWindow.TryCreate(mode, layer.Display.Bounds, hosts, device!) is not { } better)
            {
                continue;
            }

            OpacityCurve opacity = layer.WindowOpacity.Curve;
            layer.Detach();
            if (!layer.Attach(better))
            {
                layer.Detach();
                better.Dispose();
                layer.Attach(current);
                continue;
            }

            current.Dispose();
            ShowLayer(layer, opacity);
            Log.Info(LogSource, $"Display {layer.Display.Number}: the lights are drawn {mode} again.");
            dirty = true;
            return;
        }
    }

    private void ScheduleDegradedRetry(long now)
    {
        bool degraded = layers.Values.Any(l => WantsVisible(l) && (l.Window is null || l.Window.Mode != Requested));
        preferredRetryAt = degraded ? preferredRetryAt ?? now + PreferredRetryInterval : null;
    }

    private void CreateWatcherWindow()
    {
        watcher = User32.CreateWindowExW(
            ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate, WindowClasses.WatcherClass, null, WindowStyles.Popup, 0, 0, 1, 1, 0, 0, WindowClasses.Instance, 0);
        if (watcher == 0)
        {
            Log.Warn(LogSource, "The hidden watcher window could not be created; display changes are found by the 2 s check only.");
        }
    }

    private void DestroyWatcherWindow()
    {
        if (watcher != 0)
        {
            nint window = watcher;
            watcher = 0;
            User32.DestroyWindow(window);
        }
    }

    private unsafe void InstallForegroundHook() =>
        foregroundHook = User32.SetWinEventHook(
            Win32Constants.EventSystemForeground, Win32Constants.EventSystemForeground, 0, &OnWinEvent, 0, 0,
            Win32Constants.WinEventOutOfContext | Win32Constants.WinEventSkipOwnProcess);

    private void RemoveForegroundHook()
    {
        if (foregroundHook != 0)
        {
            User32.UnhookWinEvent(foregroundHook);
            foregroundHook = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time) =>
        LightsThreadContext.Guard(sink => sink.OnForegroundChanged(hwnd));

    /// <summary>The watcher window (tests post shell messages to it).</summary>
    internal nint WatcherWindow => watcher;
}
