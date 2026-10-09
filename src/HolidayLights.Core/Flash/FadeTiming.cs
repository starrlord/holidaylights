namespace HolidayLights.Core.Flash;

/// <summary>
/// Smooth fading of light bulbs in the discrete patterns (PRODUCT-SPEC 5.8): turning on ramps 0 to 1 in <c>0.6 d</c>,
/// turning off ramps 1 to 0 in <c>d</c> (an incandescent afterglow), linearly, starting at the step boundary; no step moves.
/// Classic patterns use <c>d = min(0.4 P, 150 ms)</c> (120 ms at the default speed, 24 ms at the fastest), Twinkle
/// <c>d = 0.8 P</c>, Chase Around the Screen <c>d = 0.5 P</c>. Every fade ends within its step.
/// </summary>
internal static class FadeTiming
{
    /// <summary>The longest classic fade.</summary>
    public const double ClassicMaximumMilliseconds = 150;

    /// <summary>The fade durations of a pattern at a step period.</summary>
    /// <param name="pattern">The pattern playing (for Combination: the pattern of the current segment).</param>
    /// <param name="stepPeriodMilliseconds">P.</param>
    /// <returns>The durations; instant for the continuous and music-driven patterns, whose brightness is not a fade.</returns>
    public static FadeProfile For(FlashPatternId pattern, double stepPeriodMilliseconds)
    {
        double period = Math.Max(0, stepPeriodMilliseconds);
        double d = pattern switch
        {
            _ when FlashPatterns.IsClassic(pattern) => Math.Min(0.4 * period, ClassicMaximumMilliseconds),
            FlashPatternId.Twinkle => 0.8 * period,
            FlashPatternId.ChaseAround => 0.5 * period,
            _ => 0,
        };
        return d > 0 ? new FadeProfile(0.6 * d, d) : FadeProfile.Instant;
    }

    /// <summary>The brightness during a fade from one step's target to the next.</summary>
    /// <param name="from">The brightness at the end of the previous step.</param>
    /// <param name="to">The target of the current step.</param>
    /// <param name="elapsedMilliseconds">Time since the step began.</param>
    /// <param name="profile">The durations.</param>
    /// <returns>The brightness.</returns>
    public static float Resolve(float from, float to, double elapsedMilliseconds, FadeProfile profile)
    {
        double duration = to > from ? profile.TurnOnMilliseconds : profile.TurnOffMilliseconds;
        if (to == from || duration <= 0 || elapsedMilliseconds >= duration)
        {
            return to;
        }

        double progress = Math.Max(0, elapsedMilliseconds) / duration;
        return (float)(from + ((to - from) * progress));
    }
}
