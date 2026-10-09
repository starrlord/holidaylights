using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>Screen saver movement "Style" (5.4 "Screen Saver Movement Type" 0-3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaverMovementStyle>))]
public enum SaverMovementStyle
{
    /// <summary>"Bounce Off Sides" (0).</summary>
    [JsonStringEnumMemberName("bounceOffSides")]
    BounceOffSides = 0,

    /// <summary>"Gravity Well" (1).</summary>
    [JsonStringEnumMemberName("gravityWell")]
    GravityWell = 1,

    /// <summary>"Falling Leaves" (2).</summary>
    [JsonStringEnumMemberName("fallingLeaves")]
    FallingLeaves = 2,

    /// <summary>"Attraction" (3).</summary>
    [JsonStringEnumMemberName("attraction")]
    Attraction = 3,
}

/// <summary>Background picture "Placement" (5.4 "Screen Saver Picture Display Type").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PicturePlacement>))]
public enum PicturePlacement
{
    /// <summary>"Center" (0): x centred; y at one third of the free height when the picture is shorter than the screen (5.4).</summary>
    [JsonStringEnumMemberName("center")]
    Center = 0,

    /// <summary>"Tile" (1).</summary>
    [JsonStringEnumMemberName("tile")]
    Tile = 1,

    /// <summary>"Stretch" (2).</summary>
    [JsonStringEnumMemberName("stretch")]
    Stretch = 2,

    /// <summary>"Fit" (NICE): the largest size without distortion, background colour around it.</summary>
    [JsonStringEnumMemberName("fit")]
    Fit = 3,
}

/// <summary>The screen saver message font (5.4 LOGFONT reduced to what the UI offers).</summary>
public sealed record SaverFont
{
    /// <summary>Font family; a missing family uses the look-alike rules of PRODUCT-SPEC 6.2.3.</summary>
    public string Family { get; set; } = "Arial";

    /// <summary>Size in points, 18-100 (36 pt = 48 DIP; 5.4 stored -48 px).</summary>
    public int SizePt { get; set; } = 36;

    /// <summary>Bold (5.4 <c>lfWeight</c> &gt;= 600).</summary>
    public bool Bold { get; set; } = true;

    /// <summary>Italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Underline.</summary>
    public bool Underline { get; set; }

    /// <summary>Strikeout.</summary>
    public bool Strikeout { get; set; }
}

/// <summary>The screen saver values that themes hold (8 of the 13 theme values), all with their 5.4 defaults.</summary>
public sealed record SaverLook
{
    /// <summary>
    /// The animation: <see cref="SaverAnimations.None"/>, one of the 24 built-in animation names ("Snow", ...), or
    /// <see cref="SaverAnimations.ForBulb"/> for an add-on bulb.
    /// </summary>
    public string Animation { get; set; } = SaverAnimations.Snow;

    /// <summary>"Style" (ignored for animations with their own movement; see <see cref="SaverAnimations.HasOwnMovement"/>).</summary>
    public SaverMovementStyle Style { get; set; } = SaverMovementStyle.BounceOffSides;

    /// <summary>The text message (up to 255 characters; leading spaces and blank lines removed).</summary>
    public string Message { get; set; } = "Happy Holidays!";

    /// <summary>The message font.</summary>
    public SaverFont Font { get; set; } = new();

    /// <summary>The message colour.</summary>
    public RgbColor Color { get; set; } = RgbColor.Red;

    /// <summary>"Background Color".</summary>
    public RgbColor Background { get; set; } = RgbColor.Black;

    /// <summary>The background picture id (<see cref="MediaIds"/>) or <see cref="SaverPictures.None"/>.</summary>
    public string Picture { get; set; } = SaverPictures.Default;

    /// <summary>"Placement".</summary>
    public PicturePlacement Placement { get; set; } = PicturePlacement.Center;
}

/// <summary>Music values of the current settings: the 5.4 "Disabled Music" model (new songs start checked).</summary>
public sealed record CurrentMusic
{
    /// <summary>Ids of the <b>unchecked</b> songs (<see cref="MediaIds"/>).</summary>
    public IReadOnlyList<string> DisabledSongs { get; set; } = [];

    /// <summary>"Play the Chosen Songs".</summary>
    public PlayMode Mode { get; set; } = PlayMode.Always;
}

/// <summary>
/// The 13 values a theme replaces (5.4; PRODUCT-SPEC 6.4.1), as stored in the current settings (<c>current</c>):
/// arrangement, flash pattern, flash interval, checked songs (stored as unchecked songs), play mode and the 8 screen
/// saver values. Everything else is a device preference.
/// </summary>
public sealed record ThemeableSettings
{
    /// <summary>The arrangement (8 boxes).</summary>
    public SlotAssignment Arrangement { get; set; } = SlotAssignment.Classic54Default;

    /// <summary>Flash pattern and interval.</summary>
    public FlashSettings Flash { get; set; } = new();

    /// <summary>Unchecked songs and the play mode.</summary>
    public CurrentMusic Music { get; set; } = new();

    /// <summary>The screen saver look.</summary>
    public SaverLook Saver { get; set; } = new();
}

/// <summary>Screen saver animation names (5.4 "Screen Saver Module" values) and their rules.</summary>
public static class SaverAnimations
{
    /// <summary>"(None)": background, bulbs and text only.</summary>
    public const string None = "(None)";

    /// <summary>"Snow" (the default).</summary>
    public const string Snow = "Snow";

    /// <summary>"Snow Flakes".</summary>
    public const string SnowFlakes = "Snow Flakes";

    /// <summary>"Balloons".</summary>
    public const string Balloons = "Balloons";

    /// <summary>"Leaves".</summary>
    public const string Leaves = "Leaves";

    /// <summary>Prefix of an add-on bulb used as the animation: <c>bulb:&lt;bulb id&gt;</c>.</summary>
    public const string BulbPrefix = "bulb:";

    /// <summary>The 25 built-in choices in the 5.4 list order (the sorted list box): "(None)" first.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        None, "Angel", Balloons, "Baubles", "Carolers", "Dancing Demon", "Dreidels", "Easter Eggs", "Flags", "Flight Lights",
        "Gingerbread Man", "Halloween", "Happy Faces", "Heavens Above", Leaves, "Santa", "Schtanna", "Shamrocks",
        "Singing Tree", "Skeleton", Snow, "Snow Family", SnowFlakes, "Thanksgiving", "Valentine's Hearts",
    ];

    /// <summary>The value for an add-on bulb animation.</summary>
    /// <param name="bulbId">An add-on bulb id.</param>
    /// <returns><c>bulb:&lt;bulb id&gt;</c>.</returns>
    public static string ForBulb(string bulbId) => BulbPrefix + bulbId;

    /// <summary>Extracts the bulb id of a bulb animation.</summary>
    /// <param name="animation">An animation value.</param>
    /// <param name="bulbId">The bulb id when the value is a bulb animation.</param>
    /// <returns>True for a bulb animation.</returns>
    public static bool TryGetBulbId(string animation, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? bulbId)
    {
        bulbId = animation.StartsWith(BulbPrefix, StringComparison.Ordinal) && animation.Length > BulbPrefix.Length
            ? animation[BulbPrefix.Length..]
            : null;
        return bulbId is not null;
    }

    /// <summary>True for animations with their own movement, for which "Style" is disabled: (None), Snow, Snow Flakes, Balloons.</summary>
    /// <param name="animation">An animation value.</param>
    /// <returns>True when the style does not apply.</returns>
    public static bool HasOwnMovement(string animation) =>
        animation is None or Snow or SnowFlakes or Balloons;

    /// <summary>
    /// The 5.4 style rule: Leaves selects Falling Leaves; Easter Eggs or Happy Faces select Gravity Well; Halloween or Heavens
    /// Above select Attraction; anything else Bounce Off Sides. Used when a value lacks a style (5.4 default) and when the
    /// user picks one of those animations.
    /// </summary>
    /// <param name="animation">An animation value.</param>
    /// <returns>The derived style.</returns>
    public static SaverMovementStyle DefaultStyleFor(string animation) => animation switch
    {
        Leaves => SaverMovementStyle.FallingLeaves,
        "Easter Eggs" or "Happy Faces" => SaverMovementStyle.GravityWell,
        "Halloween" or "Heavens Above" => SaverMovementStyle.Attraction,
        _ => SaverMovementStyle.BounceOffSides,
    };

    /// <summary>True when picking this animation changes the style (the four animations of <see cref="DefaultStyleFor"/>); other animations keep the current style (5.4).</summary>
    /// <param name="animation">An animation value.</param>
    /// <returns>True for Leaves, Easter Eggs, Happy Faces, Halloween and Heavens Above.</returns>
    public static bool SelectsStyle(string animation) =>
        animation is Leaves or "Easter Eggs" or "Happy Faces" or "Halloween" or "Heavens Above";
}

/// <summary>Screen saver picture values.</summary>
public static class SaverPictures
{
    /// <summary>"(None)": no background picture.</summary>
    public const string None = "(None)";

    /// <summary>The 5.4 default picture, Santa Candle.</summary>
    public const string Default = "bundled:Santa Candle.BMP";
}
