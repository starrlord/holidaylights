namespace HolidayLights.Platform.Power;

/// <summary>
/// Which displays a full-screen app covers completely (PRODUCT-SPEC 5.12.2). The desktop, the taskbars, shell overlays,
/// cloaked, minimized and invisible windows and this process's own windows never count. A window's extended frame
/// bounds (DWM, physical pixels, without the invisible resize borders) must contain the display's whole
/// <c>rcMonitor</c>, so a maximized window (which covers only the work area) is not full screen.
/// </summary>
/// <remarks>
/// <para>A display is full screen while the foreground window covers it. It stays full screen after that window loses
/// the focus to a window on another display (a full-screen video on Display 2 while the user works on Display 1) for
/// as long as the window still covers it, until the user activates another window that lies mostly on that display.
/// Only windows the user activated in full screen count, so transparent overlays that cover a display (game and
/// recording overlays, screen-sharing borders) never hide the lights.</para>
/// <para>Not thread safe: <see cref="PauseSignalSource"/> calls <see cref="Detect"/> from one poll at a time.</para>
/// </remarks>
internal sealed class FullScreenDetector
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private readonly IWindowProbe probe;

    /// <summary>The full-screen window kept for each display id after it lost the focus.</summary>
    private readonly Dictionary<string, nint> kept = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a detector for the real windows.</summary>
    public FullScreenDetector()
        : this(NativeWindowProbe.Instance)
    {
    }

    /// <summary>Creates a detector over a window probe (tests).</summary>
    /// <param name="probe">The windows.</param>
    internal FullScreenDetector(IWindowProbe probe)
    {
        this.probe = probe;
    }

    /// <summary>The displays that a window rectangle covers completely (pure).</summary>
    /// <param name="window">The window rectangle, physical pixels.</param>
    /// <param name="displays">The displays.</param>
    /// <returns>The covered display ids.</returns>
    public static IReadOnlySet<string> CoveredDisplays(RectI window, IReadOnlyList<DisplayInfo> displays)
    {
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DisplayInfo display in displays)
        {
            if (window.Contains(display.Bounds))
            {
                covered.Add(display.DeviceId);
            }
        }

        return covered.Count == 0 ? None : covered;
    }

    /// <summary>The display that shows most of a window rectangle (pure).</summary>
    /// <param name="window">The window rectangle, physical pixels.</param>
    /// <param name="displays">The displays.</param>
    /// <returns>The display, or null when the window lies on none.</returns>
    public static DisplayInfo? MainDisplay(RectI window, IReadOnlyList<DisplayInfo> displays)
    {
        DisplayInfo? best = null;
        long bestArea = 0;
        foreach (DisplayInfo display in displays)
        {
            RectI overlap = window.Intersect(display.Bounds);
            long area = (long)overlap.Width * overlap.Height;
            if (area > bestArea)
            {
                best = display;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>The ids of the displays that a full-screen app covers now.</summary>
    /// <param name="displays">The displays.</param>
    /// <returns>The covered display ids (empty for none).</returns>
    public IReadOnlySet<string> Detect(IReadOnlyList<DisplayInfo> displays)
    {
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        nint foreground = probe.Foreground;
        if (foreground != 0 && probe.Inspect(foreground) is { } front)
        {
            IReadOnlySet<string> full = front.IsOwn ? None : CoveredDisplays(front.Bounds, displays);
            foreach (string id in full)
            {
                covered.Add(id);
                kept[id] = foreground;
            }

            // The user works in another window on that display: whatever was full screen there is behind it now.
            if (full.Count == 0 && MainDisplay(front.Bounds, displays) is { } main)
            {
                kept.Remove(main.DeviceId);
            }
        }

        foreach ((string id, nint window) in kept.ToArray())
        {
            if (covered.Contains(id))
            {
                continue;
            }

            DisplayInfo? display = Find(displays, id);
            if (display is not null && probe.Inspect(window) is { IsOwn: false } facts && facts.Bounds.Contains(display.Bounds))
            {
                covered.Add(id);
            }
            else
            {
                // Closed, minimized, moved to another virtual desktop, out of full screen, or the display is gone.
                kept.Remove(id);
            }
        }

        return covered.Count == 0 ? None : covered;
    }

    private static DisplayInfo? Find(IReadOnlyList<DisplayInfo> displays, string id)
    {
        foreach (DisplayInfo display in displays)
        {
            if (string.Equals(display.DeviceId, id, StringComparison.OrdinalIgnoreCase))
            {
                return display;
            }
        }

        return null;
    }
}
