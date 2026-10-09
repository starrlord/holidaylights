namespace HolidayLights.App.Controls;

/// <summary>
/// The content of a radio button or check box with a secondary line under its label ("On Desktop" / "Below all your
/// windows."), shown with the <c>HL.Template.TwoLine</c> template so both lines follow the control's enabled state.
/// </summary>
public sealed class TwoLineLabel
{
    /// <summary>The label with its access key ("On _Desktop").</summary>
    public string Label { get; set; } = "";

    /// <summary>The secondary line, or null.</summary>
    public string? Description { get; set; }

    /// <summary>The label without the access-key marker (what screen readers read).</summary>
    /// <returns>The label.</returns>
    public override string ToString() => Label.Replace("_", "", StringComparison.Ordinal);
}
