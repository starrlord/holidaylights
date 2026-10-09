namespace HolidayLights.Rendering;

/// <summary>A snapshot of the presenter's internals (Copy Version Info, the log, performance checks of PRODUCT-SPEC 5.14).</summary>
public sealed record LightsDiagnostics
{
    /// <summary>The state before the Lights thread started.</summary>
    public static LightsDiagnostics Empty { get; } = new();

    /// <summary>The graphics adapter ("NVIDIA GeForce RTX 4090"), or empty without a device.</summary>
    public string Adapter { get; init; } = "";

    /// <summary>True when drawing with WARP.</summary>
    public bool SoftwareRendering { get; init; }

    /// <summary>The detected desktop structure ("Raised", "Classic", "NotSplit", "NoShell").</summary>
    public string DesktopLayout { get; init; } = "";

    /// <summary>One entry per layer window.</summary>
    public IReadOnlyList<LayerDiagnostics> Layers { get; init; } = [];

    /// <summary>The <see cref="LightsScene.Revision"/> of the scene the visuals show (-1 before the first scene).</summary>
    public long SceneRevision { get; init; } = -1;

    /// <summary>Flash steps processed by the Lights thread since it started (steps that only repeating animations show are not processed).</summary>
    public long Steps { get; init; }

    /// <summary>True while the step clock runs (some display shows lights and no power-up is still waiting to start the pattern).</summary>
    public bool ClockRunning { get; init; }

    /// <summary>Light bulbs of a classic pattern that follow one repeating animation of their strip's period (no work per step).</summary>
    public int PeriodicLightBulbs { get; init; }

    /// <summary>DirectComposition commits since the thread started.</summary>
    public long Commits { get; init; }

    /// <summary>CPU time the Lights thread has used (user + kernel, scheduler-tick granularity).</summary>
    public TimeSpan ThreadCpuTime { get; init; }

    /// <summary>CPU cycles the Lights thread has used (<c>QueryThreadCycleTime</c>, exact).</summary>
    public ulong ThreadCycles { get; init; }

    /// <summary>Wall time the Lights thread spent awake (between its wake-ups and its next wait).</summary>
    public TimeSpan BusyTime { get; init; }

    /// <summary>Wall time since the Lights thread started.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>The largest delay between a step boundary and its processing, in milliseconds.</summary>
    public double MaxStepLatencyMilliseconds { get; init; }

    /// <summary>Uploaded sprite surfaces.</summary>
    public int Surfaces { get; init; }

    /// <summary>DirectComposition visuals in use.</summary>
    public int Visuals { get; init; }

    /// <summary>Graphics devices lost (and recreated) this session.</summary>
    public int DeviceLosses { get; init; }

    /// <summary>How long the last scene took to prepare on the thread pool, in milliseconds.</summary>
    public double LastPreparationMilliseconds { get; init; }

    /// <summary>Placements of the last scene that could not be drawn (missing or damaged bulbs).</summary>
    public int SkippedBulbs { get; init; }
}

/// <summary>One layer window in <see cref="LightsDiagnostics"/>.</summary>
/// <param name="DisplayId">The display.</param>
/// <param name="Mode">The layer mode of its window, or null without a window.</param>
/// <param name="Bounds">The window rectangle (physical pixels).</param>
/// <param name="Visible">True while shown.</param>
/// <param name="Bulbs">Bulbs in its current scene.</param>
/// <param name="Window">The window handle (diagnostics only).</param>
public sealed record LayerDiagnostics(string DisplayId, LayerMode? Mode, RectI Bounds, bool Visible, int Bulbs, long Window);
