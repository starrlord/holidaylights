namespace HolidayLights.Audio.Scheduling;

/// <summary>The random numbers of the Music Box (shuffle bag, "Intermittently" gaps). Tests substitute 5.4's MSVC <c>rand()</c>.</summary>
internal interface IMusicRandom
{
    /// <summary>A random integer from 0 to <paramref name="maxExclusive"/> - 1.</summary>
    /// <param name="maxExclusive">The exclusive upper bound (at least 1).</param>
    /// <returns>The number.</returns>
    int Next(int maxExclusive);
}

/// <summary>The production random source (<see cref="Random.Shared"/>; thread-safe).</summary>
internal sealed class SharedMusicRandom : IMusicRandom
{
    /// <summary>The instance.</summary>
    public static SharedMusicRandom Instance { get; } = new();

    /// <inheritdoc />
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}
