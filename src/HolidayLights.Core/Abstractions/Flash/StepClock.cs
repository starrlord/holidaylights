using System.Diagnostics;

namespace HolidayLights.Core.Abstractions;

/// <summary>
/// The global step clock (PRODUCT-SPEC 5.5.1): step <c>s</c> advances every <c>P = Interval x 60 ms</c>. One immutable
/// value describes it completely, so the Lights thread, every preview and the screen saver compute the same step from a
/// timestamp and blink in unison.
/// </summary>
/// <remarks>
/// <para>Timestamps are <see cref="Stopwatch.GetTimestamp"/> values (QueryPerformanceCounter), which are consistent across
/// processes on one machine (the screen saver process uses them too).</para>
/// <para>The step <i>position</i> <c>x = s + fraction</c> drives continuous patterns: Slow Glow is
/// <c>b = 0.5 - 0.5 cos(2 pi x / 16)</c>, which equals the spec's <c>t / (16 P)</c> form and stays continuous across
/// speed changes.</para>
/// <para>A speed change takes effect at the next step boundary without resetting <c>s</c> (<see cref="WithInterval"/>);
/// the clock keeps the previous segment so that positions before that boundary stay exact. A new pattern starts a new
/// clock at step 0 (<see cref="Start"/>).</para>
/// </remarks>
public sealed record StepClock
{
    /// <summary>The 5.4 tick: 60 ms.</summary>
    public const int TickMilliseconds = 60;

    /// <summary>Timestamp at which <see cref="EpochStep"/> begins.</summary>
    public required long EpochTimestamp { get; init; }

    /// <summary>The step that begins at <see cref="EpochTimestamp"/>.</summary>
    public required long EpochStep { get; init; }

    /// <summary>Ticks per step from <see cref="EpochTimestamp"/> on (1-9; Don't Flash has no running clock).</summary>
    public required int Interval { get; init; }

    /// <summary>The segment before <see cref="EpochTimestamp"/> (only after a speed change), else null.</summary>
    public StepClock? Previous { get; init; }

    /// <summary>The step period P in milliseconds (<c>Interval x 60</c>).</summary>
    public double StepPeriodMilliseconds => Interval * TickMilliseconds;

    private double PeriodTicks => StepPeriodMilliseconds * Stopwatch.Frequency / 1000.0;

    /// <summary>Starts a clock: step <paramref name="step"/> begins at <paramref name="timestamp"/>.</summary>
    /// <param name="timestamp">A <see cref="Stopwatch.GetTimestamp"/> value.</param>
    /// <param name="interval">Ticks per step, 1-9.</param>
    /// <param name="step">The step that begins at <paramref name="timestamp"/> (0 when a pattern starts).</param>
    /// <returns>The clock.</returns>
    public static StepClock Start(long timestamp, int interval, long step = 0)
    {
        ValidateInterval(interval);
        return new StepClock { EpochTimestamp = timestamp, EpochStep = step, Interval = interval };
    }

    /// <summary>Returns the step position (step plus the elapsed fraction of it) at a timestamp.</summary>
    /// <param name="timestamp">A <see cref="Stopwatch.GetTimestamp"/> value; earlier than the epoch gives positions before it.</param>
    /// <returns>The position <c>x</c>.</returns>
    public double PositionAt(long timestamp) =>
        timestamp < EpochTimestamp && Previous is not null
            ? Previous.PositionAt(timestamp)
            : EpochStep + (timestamp - EpochTimestamp) / PeriodTicks;

    /// <summary>Returns the step that is showing at a timestamp.</summary>
    /// <param name="timestamp">A <see cref="Stopwatch.GetTimestamp"/> value.</param>
    /// <returns><c>floor(PositionAt(timestamp))</c>.</returns>
    public long StepAt(long timestamp) => (long)Math.Floor(PositionAt(timestamp));

    /// <summary>Returns the timestamp at which a step begins.</summary>
    /// <param name="step">The step.</param>
    /// <returns>A <see cref="Stopwatch.GetTimestamp"/> value.</returns>
    public long TimestampOfStep(long step) =>
        step < EpochStep && Previous is not null
            ? Previous.TimestampOfStep(step)
            : EpochTimestamp + (long)Math.Round((step - EpochStep) * PeriodTicks);

    /// <summary>
    /// Changes the speed at the next step boundary after <paramref name="timestamp"/>, keeping the step count. When a change
    /// is already pending (the timestamp lies before <see cref="EpochTimestamp"/>), only its interval is replaced.
    /// </summary>
    /// <param name="interval">The new ticks per step, 1-9.</param>
    /// <param name="timestamp">When the change was requested.</param>
    /// <returns>The new clock.</returns>
    public StepClock WithInterval(int interval, long timestamp)
    {
        ValidateInterval(interval);
        if (timestamp < EpochTimestamp && Previous is not null)
        {
            return this with { Interval = interval };
        }

        long boundaryStep = StepAt(timestamp) + 1;
        return new StepClock
        {
            EpochTimestamp = TimestampOfStep(boundaryStep),
            EpochStep = boundaryStep,
            Interval = interval,
            Previous = this with { Previous = null },
        };
    }

    private static void ValidateInterval(int interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, FlashSettings.MinInterval);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, FlashSettings.MaxInterval);
    }
}

/// <summary>Publishes the current <see cref="StepClock"/> (the presenter's, or a preview's own).</summary>
public interface IStepClockSource
{
    /// <summary>The current clock.</summary>
    StepClock Clock { get; }

    /// <summary>Raised (on any thread) when the clock was restarted or its speed changed.</summary>
    event EventHandler? ClockChanged;
}
