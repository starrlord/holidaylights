using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>Which shipped set a theme belongs to (drives the Themes page groups, "Restore Original" and the "Changed" badge).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShippedThemeKind>))]
public enum ShippedThemeKind
{
    /// <summary>One of the 11 Holiday Lights 5.4 installer themes ("Holiday Lights 5.4 Themes").</summary>
    [JsonStringEnumMemberName("classic")]
    Classic,

    /// <summary>One of the 8 themes new in 6.0 ("New Themes").</summary>
    [JsonStringEnumMemberName("new")]
    New,
}

/// <summary>Flash values of a theme; a null member was not stored and takes its default when loaded.</summary>
public sealed record ThemeFlash
{
    /// <summary>The pattern.</summary>
    public FlashPatternId? Pattern { get; set; }

    /// <summary>The interval 1-9.</summary>
    public int? Interval { get; set; }
}

/// <summary>Music values of a theme: the 5.4 "Enabled Music" model.</summary>
public sealed record ThemeMusic
{
    /// <summary>Ids of the <b>checked</b> songs; loading a theme unchecks every song not named here. Null = not stored (all songs checked).</summary>
    public IReadOnlyList<string>? EnabledSongs { get; set; }

    /// <summary>"Play the Chosen Songs".</summary>
    public PlayMode? Mode { get; set; }
}

/// <summary>Screen saver values of a theme; a null member was not stored and takes its default when loaded.</summary>
public sealed record ThemeSaver
{
    /// <summary>Animation (<see cref="SaverAnimations"/>).</summary>
    public string? Animation { get; set; }

    /// <summary>Style; when null it is derived from the animation (<see cref="SaverAnimations.DefaultStyleFor"/>; the installer themes lack it).</summary>
    public SaverMovementStyle? Style { get; set; }

    /// <summary>Text message.</summary>
    public string? Message { get; set; }

    /// <summary>Message font.</summary>
    public SaverFont? Font { get; set; }

    /// <summary>Message colour.</summary>
    public RgbColor? Color { get; set; }

    /// <summary>Background colour (the installer themes lack it: black).</summary>
    public RgbColor? Background { get; set; }

    /// <summary>Picture id or <see cref="SaverPictures.None"/>.</summary>
    public string? Picture { get; set; }

    /// <summary>Placement.</summary>
    public PicturePlacement? Placement { get; set; }
}

/// <summary>
/// A theme file (<c>%APPDATA%\Holiday Lights\Themes\&lt;name&gt;.json</c>, schema <c>holidaylights.theme/1</c>, PRODUCT-SPEC
/// Appendix C): exactly the 13 values of 5.4, any of which may be missing.
/// </summary>
/// <remarks>Treat instances as immutable (setters exist only for source-generated JSON; see <see cref="AppSettings"/>).</remarks>
public sealed record ThemeDefinition
{
    /// <summary>The schema identifier.</summary>
    public const string SchemaId = "holidaylights.theme/1";

    /// <summary>The schema of the file.</summary>
    public string Schema { get; set; } = SchemaId;

    /// <summary>The theme name (1-63 characters; none of \ / : * ? " &lt; &gt; |). Names compare case-insensitively.</summary>
    public string Name { get; set; } = "";

    /// <summary>Classic or New for shipped themes (also after the user replaced their values); null for the user's own themes.</summary>
    public ShippedThemeKind? Shipped { get; set; }

    /// <summary>The arrangement, or null when not stored (the 5.4 default, Christmas 1, then applies).</summary>
    public SlotAssignment? Arrangement { get; set; }

    /// <summary>Flash pattern and interval.</summary>
    public ThemeFlash? Flash { get; set; }

    /// <summary>Checked songs and play mode.</summary>
    public ThemeMusic? Music { get; set; }

    /// <summary>Screen saver values.</summary>
    public ThemeSaver? Saver { get; set; }
}

/// <summary>Names of the 19 shipped themes (PRODUCT-SPEC 7.2), exactly as shown.</summary>
public static class ShippedThemeNames
{
    /// <summary>"Blank Slate".</summary>
    public const string BlankSlate = "Blank Slate";

    /// <summary>"Chanukah".</summary>
    public const string Chanukah = "Chanukah";

    /// <summary>"Christmas 1" (the 5.4 default look).</summary>
    public const string Christmas1 = "Christmas 1";

    /// <summary>"Christmas 2".</summary>
    public const string Christmas2 = "Christmas 2";

    /// <summary>"Easter Eggs".</summary>
    public const string EasterEggs = "Easter Eggs";

    /// <summary>"Halloween".</summary>
    public const string Halloween = "Halloween";

    /// <summary>"July 4th".</summary>
    public const string July4th = "July 4th";

    /// <summary>"New Year".</summary>
    public const string NewYear = "New Year";

    /// <summary>"St. Patrick's Day".</summary>
    public const string StPatricksDay = "St. Patrick's Day";

    /// <summary>"Thanksgiving".</summary>
    public const string Thanksgiving = "Thanksgiving";

    /// <summary>"Valentine's Day".</summary>
    public const string ValentinesDay = "Valentine's Day";

    /// <summary>"Autumn Harvest".</summary>
    public const string AutumnHarvest = "Autumn Harvest";

    /// <summary>"Bubble Lights".</summary>
    public const string BubbleLights = "Bubble Lights";

    /// <summary>"Christmas Twinkle".</summary>
    public const string ChristmasTwinkle = "Christmas Twinkle";

    /// <summary>"Classic Lights" (the default between holidays).</summary>
    public const string ClassicLights = "Classic Lights";

    /// <summary>"Holiday Party".</summary>
    public const string HolidayParty = "Holiday Party";

    /// <summary>"Spring Garden".</summary>
    public const string SpringGarden = "Spring Garden";

    /// <summary>"Summer Nights".</summary>
    public const string SummerNights = "Summer Nights";

    /// <summary>"Winter Wonderland".</summary>
    public const string WinterWonderland = "Winter Wonderland";

    /// <summary>The 11 classic installer themes, A-Z.</summary>
    public static IReadOnlyList<string> Classic { get; } =
        [BlankSlate, Chanukah, Christmas1, Christmas2, EasterEggs, Halloween, July4th, NewYear, StPatricksDay, Thanksgiving, ValentinesDay];

    /// <summary>The 8 new themes, A-Z.</summary>
    public static IReadOnlyList<string> New { get; } =
        [AutumnHarvest, BubbleLights, ChristmasTwinkle, ClassicLights, HolidayParty, SpringGarden, SummerNights, WinterWonderland];
}
