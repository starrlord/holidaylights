namespace HolidayLights.Core.Flash;

/// <summary>
/// The timing rules of the lights (PRODUCT-SPEC 5.5): a 60 ms tick, a step every
/// <c>Flash Interval</c> ticks (1-9; 5.4 forced "Don't Flash" to 10), one global step counter, and each strip showing its
/// frame <c>step mod frames</c>. <see cref="StepClock"/> carries the running clock; these are the rules around it.
/// </summary>
public static class FlashClock
{
    /// <summary>The 5.4 tick (<c>SetTimer(main, 1, 60)</c>).</summary>
    public const int TickMilliseconds = StepClock.TickMilliseconds;

    /// <summary>Ticks per step of "Don't Flash" in 5.4 (an unchanged picture redrawn every 600 ms).</summary>
    public const int DontFlashTicks = 10;

    /// <summary>The smallest interval while "Limit Flashing to 3 Flashes per Second" is on (180 ms; PRODUCT-SPEC 5.5.3).</summary>
    public const int LimitedMinimumInterval = 3;

    /// <summary>While flashing is limited, a bulb is retriggered at most this often (Twinkle, Chase Around, Dance).</summary>
    public const double LimitedRetriggerMilliseconds = 1000.0 / 3;

    /// <summary>Ticks per step: <paramref name="interval"/> (clamped to 1-9), or 10 for "Don't Flash".</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="interval">"Flash Interval" (5.4 does not range-check it on load).</param>
    /// <returns>Ticks of 60 ms per step.</returns>
    public static int TicksPerStep(FlashPatternId pattern, int interval) =>
        pattern == FlashPatternId.DontFlash ? DontFlashTicks : Math.Clamp(interval, FlashSettings.MinInterval, FlashSettings.MaxInterval);

    /// <summary>The step period P in milliseconds: <see cref="TicksPerStep"/> x 60 (60-540 ms; 600 ms for "Don't Flash").</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="interval">"Flash Interval".</param>
    /// <returns>P.</returns>
    public static int StepPeriodMilliseconds(FlashPatternId pattern, int interval) => TicksPerStep(pattern, interval) * TickMilliseconds;

    /// <summary>The interval in effect under the flash limit: at least 3 while limited (slider positions 8-9 off).</summary>
    /// <param name="interval">The chosen interval (1-9).</param>
    /// <param name="limitFlashing">"Limit Flashing to 3 Flashes per Second".</param>
    /// <returns>The effective interval (1-9).</returns>
    public static int LimitInterval(int interval, bool limitFlashing)
    {
        int clamped = Math.Clamp(interval, FlashSettings.MinInterval, FlashSettings.MaxInterval);
        return limitFlashing ? Math.Max(clamped, LimitedMinimumInterval) : clamped;
    }

    /// <summary>
    /// True when the lights need a running step clock: false for "Don't Flash" (a static picture in 6.0) and for the
    /// energy-saver "Stop Flashing".
    /// </summary>
    /// <param name="options">The effective flash options.</param>
    /// <returns>True when steps advance.</returns>
    public static bool RunsClock(FlashOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !options.StopFlashing && options.Pattern != FlashPatternId.DontFlash;
    }

    /// <summary>The frame a strip shows at a step: <c>step mod frames</c>, never negative.</summary>
    /// <param name="step">The global step.</param>
    /// <param name="frames">The strip's frame count (1 or more).</param>
    /// <returns>0 to <paramref name="frames"/> - 1.</returns>
    public static int StripFrame(long step, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        long frame = step % frames;
        return (int)(frame < 0 ? frame + frames : frame);
    }
}
