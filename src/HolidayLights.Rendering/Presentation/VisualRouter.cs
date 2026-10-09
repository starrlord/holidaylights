using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;

namespace HolidayLights.Rendering.Presentation;

/// <summary>
/// Routes the animator's per-ordinal changes to the bulb visuals of the current scene on visible displays. Bulbs on hidden
/// displays have no route: their changes are dropped and they receive the full state when the display is shown again.
/// </summary>
internal sealed class VisualRouter : IBulbVisualTarget
{
    private BulbVisuals?[] routes = [];

    /// <summary>The context of the current iteration (device, surfaces, images, animation batch).</summary>
    public VisualContext? Context { get; set; }

    /// <summary>Number of routed bulbs.</summary>
    public int RoutedCount { get; private set; }

    /// <summary>Replaces every route.</summary>
    /// <param name="count">Placements in the layout.</param>
    /// <param name="entries">The visuals of each routed ordinal.</param>
    public void Reset(int count, IEnumerable<(int Ordinal, BulbVisuals Visuals)> entries)
    {
        routes = new BulbVisuals?[count];
        RoutedCount = 0;
        foreach ((int ordinal, BulbVisuals visuals) in entries)
        {
            if ((uint)ordinal < (uint)routes.Length)
            {
                routes[ordinal] = visuals;
                RoutedCount++;
            }
        }
    }

    /// <summary>The visuals of an ordinal, if routed.</summary>
    public BulbVisuals? this[int ordinal] => (uint)ordinal < (uint)routes.Length ? routes[ordinal] : null;

    /// <summary>What a routed bulb shows now (to seed a new animator), or null.</summary>
    public BulbVisualState? Current(int ordinal, long now, float glowIntensity)
    {
        if (this[ordinal] is not { } visuals)
        {
            return null;
        }

        float lit = (float)visuals.LitCurve.Evaluate(now);
        float glow = glowIntensity > 0 ? (float)(visuals.GlowCurve.Evaluate(now) / glowIntensity) : lit;
        return new BulbVisualState(Math.Max(visuals.Frame, 0), lit, Math.Clamp(glow, 0f, 1f));
    }

    /// <inheritdoc />
    public void ShowFrame(int ordinal, int frame)
    {
        if (this[ordinal] is { } visuals && Context is { } context)
        {
            visuals.ShowFrame(frame, context);
        }
    }

    /// <inheritdoc />
    public void SetLit(int ordinal, OpacityCurve curve)
    {
        if (this[ordinal] is { } visuals && Context is { } context)
        {
            visuals.SetLit(curve, context);
        }
    }

    /// <inheritdoc />
    public void SetGlow(int ordinal, OpacityCurve curve)
    {
        if (this[ordinal] is { } visuals && Context is { } context)
        {
            visuals.SetGlow(curve, context);
        }
    }

    /// <inheritdoc />
    public void SetOpacity(int ordinal, OpacityCurve curve)
    {
        if (this[ordinal] is { } visuals && Context is { } context)
        {
            visuals.SetOpacity(curve, context);
        }
    }
}
