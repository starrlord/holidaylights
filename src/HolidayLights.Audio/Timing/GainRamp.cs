namespace HolidayLights.Audio.Timing;

/// <summary>A linear change of gain over time (fade-in of the first song, fade-out at exit), in clock ticks.</summary>
/// <param name="From">Gain at <paramref name="Start"/>.</param>
/// <param name="To">Gain from <paramref name="Start"/> + <paramref name="Length"/> on.</param>
/// <param name="Start">When the ramp begins.</param>
/// <param name="Length">Duration in clock ticks; 0 or less is an instant change.</param>
internal readonly record struct GainRamp(double From, double To, long Start, long Length)
{
    /// <summary>A constant gain.</summary>
    /// <param name="gain">The gain.</param>
    /// <returns>The ramp.</returns>
    public static GainRamp Constant(double gain) => new(gain, gain, 0, 0);

    /// <summary>The gain at a moment.</summary>
    /// <param name="now">The moment.</param>
    /// <returns>The gain, between <see cref="From"/> and <see cref="To"/>.</returns>
    public double At(long now)
    {
        if (Length <= 0 || now >= Start + Length)
        {
            return To;
        }

        return now <= Start ? From : From + (To - From) * (now - Start) / Length;
    }

    /// <summary>True while the gain still changes.</summary>
    /// <param name="now">The moment.</param>
    /// <returns>True before the end of the ramp.</returns>
    public bool IsRunning(long now) => Length > 0 && now < Start + Length;
}
