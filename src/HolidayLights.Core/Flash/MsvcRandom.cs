namespace HolidayLights.Core.Flash;

/// <summary>
/// The MSVC C runtime <c>srand</c>/<c>rand</c> pair that Holiday Lights 5.4 used:
/// <c>state = state x 214013 + 2531011</c> (mod 2^32), <c>rand() = (state &gt;&gt; 16) &amp; 0x7FFF</c>. Golden values:
/// <c>Golden/msvc-rand.json</c>.
/// </summary>
/// <remarks>Not thread-safe; each user owns its instance.</remarks>
public sealed class MsvcRandom
{
    /// <summary>The largest value <see cref="Next"/> returns (<c>RAND_MAX</c>).</summary>
    public const int MaxValue = 0x7FFF;

    private uint state;

    /// <summary>Seeds the generator (<c>srand(seed)</c>).</summary>
    /// <param name="seed">The seed; 5.4 used the millisecond of the system time.</param>
    public MsvcRandom(uint seed) => state = seed;

    /// <summary>Returns the next value (<c>rand()</c>).</summary>
    /// <returns>0 to <see cref="MaxValue"/>.</returns>
    public int Next()
    {
        state = unchecked((state * 214013u) + 2531011u);
        return (int)((state >> 16) & MaxValue);
    }
}
