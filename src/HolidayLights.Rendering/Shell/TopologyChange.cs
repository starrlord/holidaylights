namespace HolidayLights.Rendering.Shell;

/// <summary>
/// The difference between two monitor snapshots (PRODUCT-SPEC 5.2.3: a diff of <c>{device, rcMonitor, rcWork, dpi}</c> per
/// display), matched by GDI device name. The Lights thread uses it to notice display changes before the next scene arrives:
/// it logs them, tries the requested layer mode again and keeps the layers of vanished or moved displays hidden.
/// </summary>
/// <param name="Added">Monitors that appeared.</param>
/// <param name="Removed">Monitors that went away.</param>
/// <param name="Changed">Monitors whose rectangle, work area or DPI changed (before, after).</param>
internal sealed record TopologyChange(
    IReadOnlyList<MonitorState> Added, IReadOnlyList<MonitorState> Removed, IReadOnlyList<(MonitorState Before, MonitorState After)> Changed)
{
    /// <summary>True when nothing changed.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>Compares two snapshots.</summary>
    /// <param name="before">The previous snapshot.</param>
    /// <param name="after">The current snapshot.</param>
    /// <returns>The difference (order does not matter).</returns>
    public static TopologyChange Between(IReadOnlyList<MonitorState> before, IReadOnlyList<MonitorState> after)
    {
        var previous = new Dictionary<string, MonitorState>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorState monitor in before)
        {
            previous.TryAdd(monitor.DeviceName, monitor);
        }

        var added = new List<MonitorState>();
        var changed = new List<(MonitorState, MonitorState)>();
        foreach (MonitorState monitor in after)
        {
            if (!previous.Remove(monitor.DeviceName, out MonitorState old))
            {
                added.Add(monitor);
            }
            else if (old != monitor)
            {
                changed.Add((old, monitor));
            }
        }

        return new TopologyChange(added, [.. previous.Values], changed);
    }

    /// <summary>
    /// The displays whose layer must stay hidden until a new scene arrives: no monitor shows their rectangle any more (the
    /// display went away, moved or changed resolution), so the layer would cover the wrong pixels or straddle two monitors.
    /// </summary>
    /// <param name="layers">Each layer's display id and rectangle.</param>
    /// <param name="topology">The current monitors.</param>
    /// <returns>The ids of the stale displays.</returns>
    public static HashSet<string> StaleDisplays(IEnumerable<(string DisplayId, RectI Bounds)> layers, IReadOnlyList<MonitorState> topology) =>
        layers.Where(layer => !topology.Any(m => m.Bounds == layer.Bounds)).Select(layer => layer.DisplayId).ToHashSet(StringComparer.Ordinal);

    /// <summary>A line for the log, e.g. "\\.\DISPLAY2 removed; \\.\DISPLAY1 work area 0,0,3840,2088 -> 0,0,3840,2160".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        parts.AddRange(Added.Select(m => $"{m.DeviceName} added at {m.Bounds} ({m.Dpi} DPI)"));
        parts.AddRange(Removed.Select(m => $"{m.DeviceName} removed"));
        foreach ((MonitorState before, MonitorState after) in Changed)
        {
            var what = new List<string>();
            if (before.Bounds != after.Bounds)
            {
                what.Add($"bounds {before.Bounds} -> {after.Bounds}");
            }

            if (before.WorkArea != after.WorkArea)
            {
                what.Add($"work area {before.WorkArea} -> {after.WorkArea}");
            }

            if (before.Dpi != after.Dpi)
            {
                what.Add($"DPI {before.Dpi} -> {after.Dpi}");
            }

            parts.Add($"{after.DeviceName} {string.Join(", ", what)}");
        }

        return parts.Count == 0 ? "no change" : string.Join("; ", parts);
    }
}
