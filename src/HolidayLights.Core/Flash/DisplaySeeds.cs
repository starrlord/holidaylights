namespace HolidayLights.Core.Flash;

/// <summary>
/// The seeds of the randomized patterns per display (product-owner decision 5): identical displays never twinkle or flash
/// at random in lockstep, while the first display keeps the exact single-display sequence.
/// </summary>
/// <remarks>
/// <para>The flash engine applies <see cref="PatternSeed"/> to every ring of a layout: Twinkle (also as a Combination
/// segment) draws <c>u(i, s)</c> from the seed of the bulb's ring, so ring 0 (the only display, the first display in "Each
/// Display", the whole wreath in "All Displays Together") is unchanged and every other ring twinkles on its own. Random
/// Flashing needs no help on the desktop: one table is rolled over every display in build order, so display 2 continues
/// display 1's <c>rand()</c> sequence. Dance to the Music draws no random numbers.</para>
/// <para>A consumer that lays out each display alone, such as the screen saver (each layout has the single ring 0), passes
/// the display's index to both methods: its Twinkle then equals the desktop's Twinkle on ring <c>display</c>.</para>
/// </remarks>
public static class DisplaySeeds
{
    /// <summary>
    /// The seed of <c>u(i, s)</c> on a ring or display: <paramref name="seed"/> itself for 0, else
    /// <c>seed XOR SplitMix64(display)</c>.
    /// </summary>
    /// <param name="seed">The pattern seed (<see cref="FlashOptions.PatternSeed"/>).</param>
    /// <param name="display">The ring id (<see cref="BulbPlacement.RingId"/>) or, for a layout of one display, its index.</param>
    /// <returns>The seed to use for that ring.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="display"/> is negative.</exception>
    public static ulong PatternSeed(ulong seed, int display)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(display);
        return display == 0 ? seed : seed ^ Salt(display);
    }

    /// <summary>
    /// The Random Flashing <c>srand</c> seed of a display that rolls its own table: <paramref name="seed"/> itself for 0, else
    /// <c>seed XOR</c> the low 32 bits of <c>SplitMix64(display)</c>. The desktop does not need it (one table covers
    /// every display).
    /// </summary>
    /// <param name="seed">The Random Flashing seed (<see cref="FlashOptions.ClassicRandomSeed"/>).</param>
    /// <param name="display">The display's index (0 keeps the classic sequence).</param>
    /// <returns>The <c>srand</c> seed for that display.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="display"/> is negative.</exception>
    public static uint ClassicRandomSeed(uint seed, int display)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(display);
        return display == 0 ? seed : seed ^ (uint)Salt(display);
    }

    // SplitMix64 of small ids differs between any two of them in the upper 32 bits by far more than any ring position
    // (i << 32), so two rings never draw the same u at the same step.
    private static ulong Salt(int display) => PatternRandom.SplitMix64((ulong)display);
}
