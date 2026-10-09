namespace HolidayLights.App.Preview;

/// <summary>
/// Layouts for previews (PRODUCT-SPEC 3.0.2): the real layout engine on the real displays (physical pixels, real scale,
/// real bulb size), so counts, gaps, truncation, alignment, corners and flavor cycling match the desktop exactly.
/// </summary>
public static class PreviewLayouts
{
    /// <summary>The area and scale of a display the way the desktop frames it (work area, display scale x bulb size).</summary>
    /// <param name="display">The display.</param>
    /// <param name="size">"Bulb Size".</param>
    /// <returns>The layout target.</returns>
    public static LayoutTarget TargetFor(DisplayInfo display, BulbSize size)
    {
        ArgumentNullException.ThrowIfNull(display);
        return new LayoutTarget(display.DeviceId, display.WorkArea, ArtScale.Effective(display.Scale, size));
    }

    /// <summary>The targets of the desktop: those of the scene's layout, else every enabled display of the scene.</summary>
    /// <param name="scene">The desktop scene.</param>
    /// <param name="size">"Bulb Size" (used when the scene has no layout yet).</param>
    /// <returns>The targets in the scene's order.</returns>
    public static IReadOnlyList<LayoutTarget> DesktopTargets(LightsScene scene, BulbSize size)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Layout.Displays.Count > 0)
        {
            return [.. scene.Layout.Displays.Select(d => d.Target)];
        }

        return [.. scene.Displays.Where(d => d.Enabled).Select(d => TargetFor(d.Display, size))];
    }

    /// <summary>Lays out an arrangement the way the desktop would show it (same targets and frame mode).</summary>
    /// <param name="engine">The layout engine.</param>
    /// <param name="bulbs">Resolves bulbs.</param>
    /// <param name="scene">The desktop scene.</param>
    /// <param name="settings">The current settings (bulb size and frame mode when the scene has no layout).</param>
    /// <param name="arrangement">The arrangement to show (try-on, a theme), or null for the desktop's own.</param>
    /// <returns>The scene's own layout when nothing differs; otherwise a new layout.</returns>
    public static LightsLayout ForDesktop(ILayoutEngine engine, IBulbResolver bulbs, LightsScene scene, AppSettings settings, SlotAssignment? arrangement)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(settings);
        SlotAssignment wanted = arrangement ?? settings.Current.Arrangement;
        if (scene.Layout.Displays.Count > 0 && scene.Layout.Arrangement.Equals(wanted))
        {
            return scene.Layout;
        }

        IReadOnlyList<LayoutTarget> targets = DesktopTargets(scene, settings.Lights.Size);
        FrameMode mode = scene.Layout.Displays.Count > 0 ? scene.Layout.Mode : settings.Lights.FrameMode;
        return targets.Count == 0 ? LightsLayout.Empty : engine.Layout(targets, wanted, bulbs, mode);
    }

    /// <summary>Lays out an arrangement on one display alone (theme cards, the Welcome card, the screen saver preview).</summary>
    /// <param name="engine">The layout engine.</param>
    /// <param name="bulbs">Resolves bulbs.</param>
    /// <param name="display">The display (usually the main display).</param>
    /// <param name="size">"Bulb Size".</param>
    /// <param name="arrangement">The arrangement.</param>
    /// <returns>The layout.</returns>
    public static LightsLayout ForDisplay(ILayoutEngine engine, IBulbResolver bulbs, DisplayInfo display, BulbSize size, SlotAssignment arrangement)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(display);
        return engine.Layout([TargetFor(display, size)], arrangement, bulbs, FrameMode.EachDisplay);
    }
}
