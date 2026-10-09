namespace HolidayLights.Platform.Displays;

/// <summary>
/// The difference between two display snapshots by <see cref="DisplayInfo.DeviceId"/>, comparing the rebuild tuple
/// <c>{device, rcMonitor, rcWork, dpi}</c> of PRODUCT-SPEC 5.2.3 (numbers and names are not part of it).
/// </summary>
/// <param name="Added">Displays that appeared (current values).</param>
/// <param name="Removed">Displays that disappeared (previous values).</param>
/// <param name="Changed">Displays whose bounds, work area or DPI changed (current values).</param>
public sealed record DisplayChanges(IReadOnlyList<DisplayInfo> Added, IReadOnlyList<DisplayInfo> Removed, IReadOnlyList<DisplayInfo> Changed)
{
    /// <summary>True when nothing in the rebuild tuple changed.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>Every display id that needs a rebuild (added, removed or changed).</summary>
    public IReadOnlySet<string> AffectedIds =>
        Added.Concat(Removed).Concat(Changed).Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Compares two snapshots.</summary>
    /// <param name="previous">The displays before.</param>
    /// <param name="current">The displays after.</param>
    /// <returns>The changes (empty when the rebuild tuples are equal).</returns>
    public static DisplayChanges Compute(IReadOnlyList<DisplayInfo> previous, IReadOnlyList<DisplayInfo> current)
    {
        Dictionary<string, DisplayInfo> before = previous.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DisplayInfo> after = current.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);

        DisplayInfo[] added = [.. current.Where(d => !before.ContainsKey(d.DeviceId))];
        DisplayInfo[] removed = [.. previous.Where(d => !after.ContainsKey(d.DeviceId))];
        DisplayInfo[] changed =
        [
            .. current.Where(d => before.TryGetValue(d.DeviceId, out DisplayInfo? old) &&
                                  (old.Bounds != d.Bounds || old.WorkArea != d.WorkArea || old.Dpi != d.Dpi)),
        ];
        return new DisplayChanges(added, removed, changed);
    }
}
