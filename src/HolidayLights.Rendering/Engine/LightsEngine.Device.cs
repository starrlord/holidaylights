using System.Diagnostics;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Overlays;
using HolidayLights.Rendering.Presentation;
using HolidayLights.Rendering.Scenes;
using HolidayLights.Rendering.Shell;

namespace HolidayLights.Rendering.Engine;

/// <summary>The composition device and its recovery, overlays, status and diagnostics.</summary>
internal sealed partial class LightsEngine
{
    private CompositionDevice? device;
    private SurfaceCache? surfaces;
    private AnimationBatch? batch;
    private OverlayController? overlays;
    private long? deviceRetryAt;
    private bool reportedNoDevice;
    private LightsHealth health = LightsHealth.Starting;
    private LightsStatus publishedStatus = LightsStatus.Initial;
    private long? firstFrame;
    private long stepCount;
    private long commitCount;
    private double maxStepLatency;
    private double lastPreparationMilliseconds;

    /// <summary>The displays in HDR, where the glow is halved.</summary>
    private readonly AdvancedColorOutputs advancedColor = new();

    /// <summary>
    /// Halves the glow on the displays that run in HDR (PRODUCT-SPEC 5.9, risk R2) and restores it elsewhere. DXGI is asked
    /// again when it reports a change, or always after a display change (<paramref name="force"/>).
    /// </summary>
    private void RefreshGlowScales(bool force)
    {
        if (advancedColor.Refresh(force))
        {
            Log.Info(LogSource, advancedColor.HdrBounds.Count == 0
                ? "No display runs in HDR; the glow is drawn at full intensity."
                : $"{advancedColor.HdrBounds.Count} display(s) in HDR; the glow is drawn at half intensity there.");
        }

        if (batch is null)
        {
            return;
        }

        foreach (DisplayLayer layer in layers.Values)
        {
            float scale = advancedColor.GlowScale(layer.Display.Bounds);
            if (scale != layer.GlowScale)
            {
                layer.SetGlowScale(scale, batch);
                dirty = true;
            }
        }
    }

    /// <summary>Command: show the on-screen pill on the display under the mouse pointer (not over a full-screen app).</summary>
    public void ShowPill(PillRequest request)
    {
        if (overlays is null || batch is null || MonitorTopology.UnderCursor() is not { } monitor)
        {
            return;
        }

        if ((pause.Global & (PauseReasons.ExclusiveFullScreen | PauseReasons.Presentation)) != 0)
        {
            return;
        }

        DisplayScene? display = Scene?.Displays.FirstOrDefault(d => d.Display.Bounds == monitor.Bounds);
        if (display is not null && pause.RestingDisplayIds.Contains(display.Display.DeviceId))
        {
            return;
        }

        long now = Now;
        overlays.ShowPill(request, monitor, Scene?.Effects.ReducedMotion ?? false, now, batch);
        overlayWake = now;
        dirty = true;
    }

    /// <summary>Command: Identify - each display's number for <paramref name="duration"/>.</summary>
    public void ShowIdentify(IReadOnlyList<DisplayInfo> displays, TimeSpan duration)
    {
        if (overlays is null || batch is null)
        {
            return;
        }

        long now = Now;
        overlays.ShowIdentify(displays, duration, Scene?.Effects.ReducedMotion ?? false, now, batch);
        overlayWake = now;
        dirty = true;
    }

    /// <summary>Command (tests and diagnostics): behave as if the graphics device was lost.</summary>
    internal void SimulateDeviceLoss() => OnDeviceLost(Now, "a simulated device loss");

    /// <summary>Command (tests): publish diagnostics now.</summary>
    internal void RefreshDiagnostics() => PublishDiagnostics();

    private void TryCreateDevice(long now)
    {
        deviceRetryAt = null;
        try
        {
            device = CompositionDevice.Create(devicePolicy.UseSoftware);
        }
        catch (CompositionUnavailableException exception)
        {
            health = LightsHealth.NoGraphicsDevice;
            deviceRetryAt = now + Ticks(DeviceRecoveryPolicy.UnavailableRetry.TotalMilliseconds);
            if (!reportedNoDevice)
            {
                reportedNoDevice = true;
                Log.Warn(LogSource, "No graphics device: the lights stay hidden and Holiday Lights keeps trying every 10 s.", exception);
            }

            return;
        }

        reportedNoDevice = false;
        surfaces = new SurfaceCache(device);
        batch = new AnimationBatch(device);
        overlays = new OverlayController(device);
        health = device.IsSoftware ? LightsHealth.SoftwareRendering : LightsHealth.Running;
        Log.Info(LogSource, $"Graphics device: {device.Adapter}{(device.IsSoftware ? " (software)" : string.Empty)}.");
        RefreshHosts();
        if (deferredScene is { } deferred)
        {
            deferredScene = null;
            ApplyScene(deferred.Scene, deferred.Transition);
        }
        else
        {
            RebuildFromPreparedScene(now);
        }

        StartNextPreparation();
    }

    /// <summary>Device lost (driver update, TDR): recreate everything at once; after two losses within a minute use WARP (PRODUCT-SPEC 5.13).</summary>
    private void OnDeviceLost(long now, string reason)
    {
        bool toSoftware = devicePolicy.RecordLoss(now);
        Log.Warn(LogSource, $"The graphics device was lost ({reason}); re-creating the lights{(toSoftware ? " with software drawing" : string.Empty)}.");
        DestroyComposition();
        TryCreateDevice(now);
    }

    /// <summary>Rebuilds every layer from the current prepared scene on a new device (pixels are kept in memory).</summary>
    private void RebuildFromPreparedScene(long now)
    {
        if (prepared is null || device is null)
        {
            return;
        }

        LightsScene scene = prepared.Scene;
        SyncLayers(scene, now, reduced: true);
        RefreshGlowScales(force: false);
        VisualContext context = CreateContext(prepared);
        foreach (DisplayLayer layer in layers.Values)
        {
            var visuals = new SceneVisuals(device, layer.Origin);
            foreach (PreparedBulb bulb in prepared.BulbsOn(layer.DisplayId))
            {
                visuals.Add(bulb, context);
            }

            layer.ReplaceScene(visuals, now, batch!);
        }

        UpdateVisibility(now, 0, applyState: true);
        dirty = true;
    }

    private void DestroyComposition()
    {
        foreach (DisplayLayer layer in layers.Values)
        {
            layer.Dispose();
        }

        foreach ((DisplayLayer layer, _) in retiringLayers)
        {
            layer.Dispose();
        }

        layers.Clear();
        retiringLayers.Clear();
        movingLayers.Clear();
        router.Reset(0, []);
        router.Context = null;
        overlays?.Dispose();
        overlays = null;
        surfaces?.Dispose();
        surfaces = null;
        batch = null;
        device?.Dispose();
        device = null;
        desktopRaised = false;
    }

    private void PublishClock() => host.PublishClock(clock.Clock);

    private void PublishStatus(bool force)
    {
        var displays = new List<DisplayLayerStatus>();
        foreach (DisplayScene display in Scene?.Displays ?? [])
        {
            if (!display.Enabled)
            {
                continue;
            }

            string id = display.Display.DeviceId;
            bool resting = pause.IsGloballyPaused || pause.RestingDisplayIds.Contains(id);
            LayerMode? effective = layers.TryGetValue(id, out DisplayLayer? layer) && layer.IsVisible ? layer.Window?.Mode : null;
            displays.Add(new DisplayLayerStatus(id, effective, resting));
        }

        var status = new LightsStatus
        {
            Health = health,
            Requested = Requested,
            Displays = displays,
            Paused = pause.Global,
            RestartCount = host.RestartCount,
            FirstFrameTimestamp = firstFrame,
        };

        if (force || !SameStatus(status, publishedStatus))
        {
            publishedStatus = status;
            host.PublishStatus(status);
        }
    }

    private static bool SameStatus(LightsStatus a, LightsStatus b) =>
        a.Health == b.Health && a.Requested == b.Requested && a.Paused == b.Paused && a.RestartCount == b.RestartCount
        && a.FirstFrameTimestamp == b.FirstFrameTimestamp && a.Displays.SequenceEqual(b.Displays);

    private void PublishDiagnostics()
    {
        nint thread = Kernel32.GetCurrentThread();
        TimeSpan cpu = Kernel32.GetThreadTimes(thread, out _, out _, out long kernel, out long user) ? TimeSpan.FromTicks(kernel + user) : TimeSpan.Zero;
        ulong cycles = Kernel32.QueryThreadCycleTime(thread, out ulong used) ? used : 0;

        host.PublishDiagnostics(new LightsDiagnostics
        {
            Adapter = device?.Adapter ?? string.Empty,
            SoftwareRendering = device?.IsSoftware ?? false,
            DesktopLayout = hosts.Layout.ToString(),
            Layers = [.. layers.Values.Select(l => new LayerDiagnostics(
                l.DisplayId, l.Window?.Mode, l.Display.Bounds, l.IsVisible, l.Scene?.Bulbs.Count ?? 0, l.Window?.Hwnd ?? 0))],
            SceneRevision = Scene?.Revision ?? -1,
            Steps = stepCount,
            ClockRunning = clock.IsRunning,
            PeriodicLightBulbs = animator?.PeriodicLightBulbs ?? 0,
            Commits = commitCount,
            ThreadCpuTime = cpu,
            ThreadCycles = cycles,
            BusyTime = TimeSpan.FromSeconds(busyTicks / (double)Stopwatch.Frequency),
            Uptime = TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - startedAt) / (double)Stopwatch.Frequency),
            MaxStepLatencyMilliseconds = maxStepLatency,
            Surfaces = surfaces?.Count ?? 0,
            Visuals = layers.Values.Sum(l => l.VisualCount),
            DeviceLosses = devicePolicy.LossCount,
            LastPreparationMilliseconds = lastPreparationMilliseconds,
            SkippedBulbs = prepared?.SkippedBulbs ?? 0,
        });
    }
}
