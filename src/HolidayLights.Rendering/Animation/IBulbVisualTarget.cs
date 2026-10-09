using HolidayLights.Rendering.Composition;

namespace HolidayLights.Rendering.Animation;

/// <summary>
/// Where the animator sends per-bulb changes, by placement ordinal. The presenter routes them to the DirectComposition visuals
/// of the bulb's display (and drops them for displays that are hidden; those get the full state when they are shown again).
/// </summary>
internal interface IBulbVisualTarget
{
    /// <summary>Shows a frame (animation and static bulbs).</summary>
    void ShowFrame(int ordinal, int frame);

    /// <summary>Sets the lit visual's opacity of a light bulb (<c>b</c>).</summary>
    void SetLit(int ordinal, OpacityCurve curve);

    /// <summary>Sets the glow visual's opacity (<c>b x intensity</c>, already scaled).</summary>
    void SetGlow(int ordinal, OpacityCurve curve);

    /// <summary>Sets the opacity of the whole bulb (the power-up's dim start of non-light bulbs).</summary>
    void SetOpacity(int ordinal, OpacityCurve curve);
}
