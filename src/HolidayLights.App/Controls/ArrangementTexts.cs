using System.Globalization;

namespace HolidayLights.App.Controls;

/// <summary>The words of the arrangement (PRODUCT-SPEC 4.8): "edge" and "corner" for the boxes, sentence and Title Case forms.</summary>
public static class ArrangementTexts
{
    /// <summary>"top edge", "left edge" (sentences).</summary>
    /// <param name="side">The edge.</param>
    /// <returns>The words.</returns>
    public static string EdgeName(Side side) => SideWord(side) + " edge";

    /// <summary>"Top Edge" (buttons, captions).</summary>
    /// <param name="side">The edge.</param>
    /// <returns>The words.</returns>
    public static string EdgeTitle(Side side) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(SideWord(side)) + " Edge";

    /// <summary>"top", "right", "bottom", "left".</summary>
    /// <param name="side">The edge.</param>
    /// <returns>The word.</returns>
    public static string SideWord(Side side) => side switch
    {
        Side.Top => "top",
        Side.Right => "right",
        Side.Bottom => "bottom",
        _ => "left",
    };

    /// <summary>"top-left corner" (sentences).</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The words.</returns>
    public static string CornerName(Corner corner) => CornerWord(corner) + " corner";

    /// <summary>"Top-Left Corner" (buttons).</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The words.</returns>
    public static string CornerTitle(Corner corner) => CornerCaption(corner) + " Corner";

    /// <summary>"Top-Left" (the corner box caption).</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The words.</returns>
    public static string CornerCaption(Corner corner) => corner switch
    {
        Corner.TopLeft => "Top-Left",
        Corner.TopRight => "Top-Right",
        Corner.BottomRight => "Bottom-Right",
        _ => "Bottom-Left",
    };

    /// <summary>"top-left", "bottom-right".</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The word.</returns>
    public static string CornerWord(Corner corner) => CornerCaption(corner).ToLowerInvariant();

    /// <summary>"1st", "2nd", "3rd", "4th" ... (positions on an edge).</summary>
    /// <param name="number">A positive number.</param>
    /// <returns>The ordinal.</returns>
    public static string Ordinal(int number)
    {
        int lastTwo = number % 100;
        string suffix = lastTwo is >= 11 and <= 13 ? "th" : (number % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
        return number.ToString(CultureInfo.CurrentCulture) + suffix;
    }

    /// <summary>A list in prose: "a", "a and b", "a, b and c".</summary>
    /// <param name="items">The items.</param>
    /// <returns>The phrase.</returns>
    public static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>
    /// The frame editor's help text (PRODUCT-SPEC 3.2.12): "Top: Standard Bulbs. Right: Standard Bulbs, Snow Family. ...
    /// Corners: Jolly Holly."
    /// </summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="nameOf">The bulb name of an id.</param>
    /// <returns>The summary.</returns>
    public static string Summary(SlotAssignment arrangement, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(nameOf);
        var parts = new List<string>(5);
        foreach (Side side in CellSlots.Sides)
        {
            IReadOnlyList<string> ids = arrangement.GetEdge(side);
            string title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(SideWord(side));
            parts.Add($"{title}: {(ids.Count == 0 ? "empty" : string.Join(", ", ids.Select(nameOf)))}.");
        }

        string?[] corners = [.. CellSlots.Corners.Select(arrangement.GetCorner)];
        if (corners.All(c => c is null))
        {
            parts.Add("Corners: empty.");
        }
        else if (corners.Distinct(BulbIds.Comparer).Count() == 1)
        {
            parts.Add($"Corners: {nameOf(corners[0]!)}.");
        }
        else
        {
            parts.Add("Corners: " + string.Join(", ", CellSlots.Corners.Select(c =>
                $"{CornerWord(c)} {(arrangement.GetCorner(c) is { } id ? nameOf(id) : "empty")}")) + ".");
        }

        return string.Join(" ", parts);
    }
}
