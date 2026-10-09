namespace HolidayLights.Core.Flash;

/// <summary>
/// The flash patterns: the five classic patterns exactly (MSVC LCG for Random Flashing,
/// golden phases in <c>Golden/layout</c>), the new patterns of PRODUCT-SPEC 5.7, smooth fading, the flash limit and Dance to
/// the Music. Owner: core-layout.
/// </summary>
/// <remarks>
/// <para>What each pattern does (PRODUCT-SPEC 5.6-5.10; <c>u(i, s)</c> = SplitMix64 of the ring's seed, ring index and step,
/// where ring 0 uses the pattern seed and every other ring its own <see cref="DisplaySeeds.PatternSeed"/>):</para>
/// <list type="bullet">
/// <item>Don't Flash (frame 0), Flash Together, Alternating, Bulb Chase, Random Flashing: exactly as 5.4 (<see cref="ClassicPatterns"/>);
/// light bulbs fade between steps when Smooth Fading is on, without moving any step.</item>
/// <item>Twinkle: light bulbs twinkle on their own (about 80 % lit), animation bulbs advance at random steps; every display
/// of "Each Display" twinkles independently.</item>
/// <item>Slow Glow: every light bulb breathes <c>0.5 - 0.5 cos(2 pi t / 16P)</c>; animation bulbs step like Flash Together.</item>
/// <item>Chase Around the Screen: one light in three, moving clockwise around each ring.</item>
/// <item>Dance to the Music: light bulbs follow music events by pitch class; Slow Glow while no music plays.</item>
/// <item>Waves and Combination (nice-to-have patterns, built): travelling brightness waves; the patterns in turn, 40 steps each.</item>
/// </list>
/// <para>Pure and thread-safe; each sequencer it creates belongs to one consumer.</para>
/// </remarks>
public sealed class FlashEngine : IFlashEngine
{
    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The options name an unknown pattern.</exception>
    public IFlashSequencer CreateSequencer(LightsLayout layout, IBulbResolver bulbs, FlashOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Pattern))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Pattern, "Unknown flash pattern.");
        }

        return new FlashSequencer(layout, bulbs, options);
    }
}
