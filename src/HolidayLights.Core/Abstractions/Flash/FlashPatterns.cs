using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>
/// The flash patterns (PRODUCT-SPEC 5.6, 5.7). Values 0-4 are the 5.4 "Flash Pattern" numbers (registry, themes,
/// import); JSON stores the camel-case names (Appendix C).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlashPatternId>))]
public enum FlashPatternId
{
    /// <summary>"Don't Flash" (5.4 value 0): the bulbs stay lit.</summary>
    [JsonStringEnumMemberName("dontFlash")]
    DontFlash = 0,

    /// <summary>"Flash Together" (5.4 value 1, default).</summary>
    [JsonStringEnumMemberName("flashTogether")]
    FlashTogether = 1,

    /// <summary>"Alternating" (5.4 value 2).</summary>
    [JsonStringEnumMemberName("alternating")]
    Alternating = 2,

    /// <summary>"Bulb Chase" (5.4 value 3).</summary>
    [JsonStringEnumMemberName("bulbChase")]
    BulbChase = 3,

    /// <summary>"Random Flashing" (5.4 value 4): 8 pre-rolled frames per strip (MSVC LCG).</summary>
    [JsonStringEnumMemberName("randomFlashing")]
    RandomFlashing = 4,

    /// <summary>"Twinkle" (new, MUST).</summary>
    [JsonStringEnumMemberName("twinkle")]
    Twinkle = 5,

    /// <summary>"Slow Glow" (new, MUST).</summary>
    [JsonStringEnumMemberName("slowGlow")]
    SlowGlow = 6,

    /// <summary>"Chase Around the Screen" (new, MUST).</summary>
    [JsonStringEnumMemberName("chaseAround")]
    ChaseAround = 7,

    /// <summary>"Dance to the Music" (new, MUST).</summary>
    [JsonStringEnumMemberName("danceToMusic")]
    DanceToMusic = 8,

    /// <summary>"Waves" (NICE; shown only when built).</summary>
    [JsonStringEnumMemberName("waves")]
    Waves = 9,

    /// <summary>"Combination" (NICE; shown only when built).</summary>
    [JsonStringEnumMemberName("combination")]
    Combination = 10,
}

/// <summary>Facts and exact UI names of the flash patterns.</summary>
public static class FlashPatterns
{
    /// <summary>The five classic patterns in 5.4 combo order.</summary>
    public static IReadOnlyList<FlashPatternId> Classic { get; } =
        [FlashPatternId.DontFlash, FlashPatternId.FlashTogether, FlashPatternId.Alternating, FlashPatternId.BulbChase, FlashPatternId.RandomFlashing];

    /// <summary>The new patterns that ship in 6.0 (MUST), in combo order.</summary>
    public static IReadOnlyList<FlashPatternId> New { get; } =
        [FlashPatternId.Twinkle, FlashPatternId.SlowGlow, FlashPatternId.ChaseAround, FlashPatternId.DanceToMusic];

    /// <summary>True for the five 5.4 patterns, which are never re-timed and never react to music.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>True for values 0-4.</returns>
    public static bool IsClassic(FlashPatternId pattern) => pattern <= FlashPatternId.RandomFlashing;

    /// <summary>The exact combo text (PRODUCT-SPEC 3.2.8).</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>E.g. "Chase Around the Screen".</returns>
    public static string DisplayName(FlashPatternId pattern) => pattern switch
    {
        FlashPatternId.DontFlash => "Don't Flash",
        FlashPatternId.FlashTogether => "Flash Together",
        FlashPatternId.Alternating => "Alternating",
        FlashPatternId.BulbChase => "Bulb Chase",
        FlashPatternId.RandomFlashing => "Random Flashing",
        FlashPatternId.Twinkle => "Twinkle",
        FlashPatternId.SlowGlow => "Slow Glow",
        FlashPatternId.ChaseAround => "Chase Around the Screen",
        FlashPatternId.DanceToMusic => "Dance to the Music",
        FlashPatternId.Waves => "Waves",
        FlashPatternId.Combination => "Combination",
        _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, null),
    };
}

/// <summary>The two theme values "Flash Pattern" and "Flash Interval" (persisted as <c>{ "pattern": ..., "interval": ... }</c>).</summary>
public sealed record FlashSettings
{
    /// <summary>Fastest interval (60 ms per step).</summary>
    public const int MinInterval = 1;

    /// <summary>Slowest interval (540 ms per step).</summary>
    public const int MaxInterval = 9;

    /// <summary>Default interval (300 ms per step).</summary>
    public const int DefaultInterval = 5;

    /// <summary>The pattern.</summary>
    public FlashPatternId Pattern { get; set; } = FlashPatternId.FlashTogether;

    /// <summary>Ticks of 60 ms per step, 1-9 (5.4 "Flash Interval").</summary>
    public int Interval { get; set; } = DefaultInterval;

    /// <summary>The 5.4 slider mapping: position p (1 slowest .. 9 fastest) gives interval <c>10 - p</c>.</summary>
    /// <param name="position">Slider position 1-9.</param>
    /// <returns>The interval.</returns>
    public static int IntervalFromSliderPosition(int position) => 10 - Math.Clamp(position, MinInterval, MaxInterval);

    /// <summary>The slider position of an interval (inverse of <see cref="IntervalFromSliderPosition"/>).</summary>
    /// <param name="interval">Interval 1-9.</param>
    /// <returns>Slider position 1-9.</returns>
    public static int SliderPositionFromInterval(int interval) => 10 - Math.Clamp(interval, MinInterval, MaxInterval);
}
