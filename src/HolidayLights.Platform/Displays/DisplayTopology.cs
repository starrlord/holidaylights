namespace HolidayLights.Platform.Displays;

/// <summary>One monitor as <c>EnumDisplayMonitors</c> reports it: the cheap part that the 2 s poll compares.</summary>
/// <param name="GdiName">GDI device name (<c>\\.\DISPLAY1</c>).</param>
/// <param name="Bounds"><c>rcMonitor</c>, physical pixels.</param>
/// <param name="WorkArea"><c>rcWork</c>, physical pixels.</param>
/// <param name="Dpi">Effective DPI.</param>
/// <param name="IsPrimary"><c>MONITORINFOF_PRIMARY</c>.</param>
internal readonly record struct MonitorSample(string GdiName, RectI Bounds, RectI WorkArea, int Dpi, bool IsPrimary);

/// <summary>The stable identity of the monitor behind a GDI device.</summary>
/// <param name="DeviceId">Device interface path (stable across restarts and re-plugging).</param>
/// <param name="FriendlyName">Monitor name from its EDID ("DELL U2723QE"), or "" when unknown.</param>
internal readonly record struct DisplayIdentity(string DeviceId, string FriendlyName);

/// <summary>Turns monitor samples into the numbered <see cref="DisplayInfo"/> list and answers point queries (pure).</summary>
internal static class DisplayTopology
{
    /// <summary>
    /// Builds the display list: the main display is number 1; the others follow in reading order of their position
    /// (left to right, then top to bottom), which stays the same across restarts as long as the arrangement does.
    /// </summary>
    /// <param name="samples">The monitors.</param>
    /// <param name="identities">Identities by GDI device name (a missing entry falls back to the GDI name).</param>
    /// <returns>The displays, main display first, then by number.</returns>
    public static IReadOnlyList<DisplayInfo> Build(IReadOnlyList<MonitorSample> samples, IReadOnlyDictionary<string, DisplayIdentity> identities)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var entries = new List<(MonitorSample Sample, string Id, string FriendlyName)>(samples.Count);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorSample sample in samples)
        {
            DisplayIdentity identity = identities.TryGetValue(sample.GdiName, out DisplayIdentity found) && found.DeviceId.Length > 0
                ? found
                : new DisplayIdentity(sample.GdiName, found.FriendlyName ?? "");
            string id = usedIds.Add(identity.DeviceId) ? identity.DeviceId : $"{identity.DeviceId}|{sample.GdiName}";
            usedIds.Add(id);
            entries.Add((sample, id, identity.FriendlyName ?? ""));
        }

        int primaryIndex = Math.Max(0, entries.FindIndex(e => e.Sample.IsPrimary));
        var ordered = new List<(MonitorSample Sample, string Id, string FriendlyName)>(entries.Count) { entries[primaryIndex] };
        ordered.AddRange(entries
            .Where((_, index) => index != primaryIndex)
            .OrderBy(e => e.Sample.Bounds.Left)
            .ThenBy(e => e.Sample.Bounds.Top)
            .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase));

        var displays = new DisplayInfo[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            (MonitorSample sample, string id, string friendlyName) = ordered[i];
            int number = i + 1;
            displays[i] = new DisplayInfo
            {
                DeviceId = id,
                DeviceName = sample.GdiName,
                FriendlyName = friendlyName.Length > 0 ? friendlyName : $"Display {number}",
                Number = number,
                Bounds = sample.Bounds,
                WorkArea = sample.WorkArea,
                Dpi = sample.Dpi,
                IsPrimary = i == 0,
            };
        }

        return displays;
    }

    /// <summary>The display that contains a point, else the nearest one.</summary>
    /// <param name="displays">The displays (at least one).</param>
    /// <param name="point">A virtual-screen point in physical pixels.</param>
    /// <returns>The display.</returns>
    public static DisplayInfo Nearest(IReadOnlyList<DisplayInfo> displays, PointI point)
    {
        DisplayInfo best = displays[0];
        long bestDistance = long.MaxValue;
        foreach (DisplayInfo display in displays)
        {
            if (display.Bounds.Contains(point))
            {
                return display;
            }

            long dx = Math.Max(Math.Max(display.Bounds.Left - point.X, point.X - (display.Bounds.Right - 1)), 0);
            long dy = Math.Max(Math.Max(display.Bounds.Top - point.Y, point.Y - (display.Bounds.Bottom - 1)), 0);
            long distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                best = display;
                bestDistance = distance;
            }
        }

        return best;
    }
}
