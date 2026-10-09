namespace HolidayLights.Core.Flash;

/// <summary>
/// The randomness of the new patterns (PRODUCT-SPEC 5.7): <c>u(i, s)</c> = a uniform number in [0, 1) from
/// <c>SplitMix64(seed XOR (i &lt;&lt; 32) XOR s)</c>, where <c>i</c> is the bulb's ring index, <c>s</c> the step and
/// <c>seed</c> the seed of the bulb's ring (<see cref="DisplaySeeds.PatternSeed"/>: the pattern seed itself on ring 0).
/// Every value is a pure function of its inputs, so patterns are deterministic and any step can be evaluated directly.
/// </summary>
internal static class PatternRandom
{
    private const double Unit = 1.0 / (1UL << 53);

    /// <summary>SplitMix64 (Steele, Lea and Flood): the output for the generator state <paramref name="x"/>.</summary>
    /// <param name="x">The state before the increment.</param>
    /// <returns>A well-mixed 64-bit value.</returns>
    public static ulong SplitMix64(ulong x)
    {
        unchecked
        {
            ulong z = x + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>The per-bulb part of the input: <c>seed XOR (i &lt;&lt; 32)</c>.</summary>
    /// <param name="seed">The seed of the bulb's ring (<see cref="DisplaySeeds.PatternSeed"/>).</param>
    /// <param name="ringIndex">The bulb's position in its ring.</param>
    /// <returns>The key.</returns>
    public static ulong Key(ulong seed, int ringIndex) => seed ^ ((ulong)(uint)ringIndex << 32);

    /// <summary><c>u(i, s)</c> for a bulb key.</summary>
    /// <param name="key">From <see cref="Key"/>.</param>
    /// <param name="step">The step (0 or more).</param>
    /// <returns>A uniform number in [0, 1).</returns>
    public static double Uniform(ulong key, long step) => (SplitMix64(key ^ (ulong)step) >> 11) * Unit;
}
