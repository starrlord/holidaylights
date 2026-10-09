namespace HolidayLights.App.BulbFactory;

/// <summary>
/// One place of a bulb in Bulb Editing: an item of the slot combo (5.4 names and order)
/// and a cell of the 3 x 3 slot map (the frame metaphor: sides and corners around the "Bulb List Preview").
/// </summary>
/// <param name="Slot">The slot.</param>
/// <param name="Name">The 5.4 name ("Top Side").</param>
/// <param name="Row">Row in the slot map (0-2).</param>
/// <param name="Column">Column in the slot map (0-2).</param>
internal sealed record SlotChoice(CellSlot Slot, string Name, int Row, int Column)
{
    /// <summary>The 9 slots in the 5.4 combo order.</summary>
    public static IReadOnlyList<SlotChoice> All { get; } =
    [
        new(CellSlot.Preview, "Bulb List Preview", 1, 1),
        new(CellSlot.Top, "Top Side", 0, 1),
        new(CellSlot.Left, "Left Side", 1, 0),
        new(CellSlot.Right, "Right Side", 1, 2),
        new(CellSlot.Bottom, "Bottom Side", 2, 1),
        new(CellSlot.TopLeft, "Top-Left Corner", 0, 0),
        new(CellSlot.TopRight, "Top-Right Corner", 0, 2),
        new(CellSlot.BottomLeft, "Bottom-Left Corner", 2, 0),
        new(CellSlot.BottomRight, "Bottom-Right Corner", 2, 2),
    ];

    /// <summary>Returns the choice of a slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The choice.</returns>
    public static SlotChoice For(CellSlot slot) => All.First(c => c.Slot == slot);

    /// <summary>Returns the choice at a slot-map position.</summary>
    /// <param name="row">Row 0-2.</param>
    /// <param name="column">Column 0-2.</param>
    /// <returns>The choice.</returns>
    public static SlotChoice At(int row, int column) => All.First(c => c.Row == row && c.Column == column);
}

/// <summary>An item of the flavor combo: "Flavor 1" ... "Flavor 8", with " *" when that flavor of the current side has an animation (5.4).</summary>
internal sealed class FlavorChoice : ObservableObject
{
    private string label;

    /// <summary>Creates the item.</summary>
    /// <param name="index">The flavor (0-7).</param>
    public FlavorChoice(int index)
    {
        Index = index;
        label = BaseName(index);
    }

    /// <summary>The flavor (0-7).</summary>
    public int Index { get; }

    /// <summary>"Flavor 3" or "Flavor 3 *".</summary>
    public string Label
    {
        get => label;
        private set => SetProperty(ref label, value);
    }

    /// <summary>Updates the in-use mark.</summary>
    /// <param name="hasAnimation">True when the flavor has an animation.</param>
    public void Update(bool hasAnimation) => Label = hasAnimation ? BaseName(Index) + BulbFactoryStrings.FlavorInUseMark : BaseName(Index);

    /// <inheritdoc />
    public override string ToString() => Label;

    private static string BaseName(int index) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, BulbFactoryStrings.FlavorFormat, index + 1);
}

/// <summary>A cell of the slot map: its slot and its first frame.</summary>
internal sealed class SlotMapCell : ObservableObject
{
    private System.Windows.Media.ImageSource? thumbnail;

    /// <summary>Creates the cell.</summary>
    /// <param name="choice">The slot.</param>
    public SlotMapCell(SlotChoice choice) => Choice = choice;

    /// <summary>The slot.</summary>
    public SlotChoice Choice { get; }

    /// <summary>Frame 0 of the slot's animation (flavor 1 for sides; the 32 x 32 list picture for the preview), or null.</summary>
    public System.Windows.Media.ImageSource? Thumbnail
    {
        get => thumbnail;
        set => SetProperty(ref thumbnail, value);
    }
}
