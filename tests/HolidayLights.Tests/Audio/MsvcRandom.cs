using HolidayLights.Audio.Scheduling;

namespace HolidayLights.Tests.Audio;

/// <summary>
/// MSVC <c>srand</c>/<c>rand()</c> (state = state x 214013 + 2531011; result = (state &gt;&gt; 16) &amp; 0x7FFF), as Holiday
/// Lights 5.4 used it (golden <c>msvc-rand.json</c>). <see cref="Next"/> is 5.4's
/// <c>rand() % n</c>, so the shuffle bag draws exactly what 5.4 drew.
/// </summary>
internal sealed class MsvcRandom : IMusicRandom
{
    private uint state;

    public MsvcRandom(uint seed) => state = seed;

    public int Rand()
    {
        state = unchecked(state * 214013 + 2531011);
        return (int)((state >> 16) & 0x7FFF);
    }

    public int Next(int maxExclusive) => Rand() % maxExclusive;
}
