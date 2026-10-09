using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Composition;

/// <summary>
/// Z-order of DirectComposition children, spelled out. <c>IDCompositionVisual::AddVisual(visual, insertAbove, reference)</c>
/// places the child in front of (<c>insertAbove</c> TRUE) or behind the reference sibling; without a reference the meaning
/// turns around: FALSE puts the child in front of every sibling and TRUE behind all of them. The live captures showed it:
/// a glow layer added before the bulb layer, both with TRUE and no reference, ended up in front of the bulbs.
/// </summary>
internal static class VisualTree
{
    /// <summary>Adds <paramref name="child"/> in front of every child <paramref name="parent"/> already has.</summary>
    public static void AddInFront(this IDCompositionVisual parent, IDCompositionVisual child) => parent.AddVisual(child, false, null);

    /// <summary>Adds <paramref name="child"/> directly in front of the existing child <paramref name="sibling"/>.</summary>
    public static void AddInFrontOf(this IDCompositionVisual parent, IDCompositionVisual child, IDCompositionVisual sibling) =>
        parent.AddVisual(child, true, sibling);
}
