namespace HolidayLights.App.Gallery;

/// <summary>
/// A placeholder of the Bulb List while the add-on bulbs are still being indexed on a first start (PRODUCT-SPEC 3.2.9:
/// "Built-in bulbs appear at once, then skeleton tiles"). It cannot be selected, dragged or used.
/// </summary>
public sealed class SkeletonItem
{
    /// <summary>The most placeholders shown after the bulbs listed so far (enough to fill a maximized list).</summary>
    public const int MaxShown = 24;

    /// <summary>Always true: the item containers turn inert for placeholders.</summary>
    public bool IsPlaceholder => true;

    /// <summary>The screen-reader name.</summary>
    public string AccessibleName => "Loading bulb";
}
