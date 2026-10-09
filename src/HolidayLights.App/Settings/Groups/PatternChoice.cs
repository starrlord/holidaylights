namespace HolidayLights.App.Settings.Groups;

/// <summary>
/// One flash pattern in the "Flash Settings" combo (PRODUCT-SPEC 3.2.8): its name, its second line and the 5-bulb demo
/// that shows it. A data item, so the open list and the closed combo each draw their own animated demo from the template.
/// </summary>
/// <param name="Pattern">The pattern.</param>
/// <param name="DemoBulbId">The demo bulb (Standard Bulbs for the classic patterns, Mini Bulbs for the new ones).</param>
/// <param name="Services">The services the demo strip draws with.</param>
public sealed record PatternChoice(FlashPatternId Pattern, string DemoBulbId, IAppServices Services)
{
    /// <summary>The pattern's name ("Flash Together").</summary>
    public string Name => FlashPatterns.DisplayName(Pattern);

    /// <summary>The second line ("All bulbs change at the same time.").</summary>
    public string Explanation => FlashSettingsGroup.Explanation(Pattern);

    /// <summary>The demo's own flash options (the pattern with smooth fading, a fixed seed).</summary>
    public FlashOptions Demo { get; } = new() { Pattern = Pattern, SmoothFading = true, PatternSeed = 7 };

    /// <inheritdoc />
    public override string ToString() => Name;
}
