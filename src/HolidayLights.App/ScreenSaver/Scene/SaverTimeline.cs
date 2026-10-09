using System.Diagnostics;

namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>
/// The fixed 60 ms simulation clock of a saver run (PRODUCT-SPEC 6.2.3): step k happens at <c>start + k x 60 ms</c>. A
/// stalled frame catches up a few steps at most and then drops the rest, so the saver never races to catch up.
/// </summary>
internal sealed class SaverTimeline
{
    /// <summary>Most steps run in one frame; beyond that the missed time is skipped.</summary>
    public const int MaxCatchUpSteps = 5;

    private readonly long start;
    private readonly long stepTicks;
    private long stepsDone;

    /// <summary>Starts the clock.</summary>
    /// <param name="startTimestamp">The start (<see cref="Stopwatch.GetTimestamp"/>).</param>
    public SaverTimeline(long startTimestamp)
        : this(startTimestamp, Stopwatch.Frequency * StepClock.TickMilliseconds / 1000)
    {
    }

    /// <summary>Starts a clock with an explicit step length (tests).</summary>
    /// <param name="startTimestamp">The start.</param>
    /// <param name="stepTicks">Timestamp ticks per step.</param>
    internal SaverTimeline(long startTimestamp, long stepTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepTicks);
        start = startTimestamp;
        this.stepTicks = stepTicks;
    }

    /// <summary>Steps counted so far (skipped ones included).</summary>
    public long StepsDone => stepsDone;

    /// <summary>Counts the steps that are due at a moment.</summary>
    /// <param name="timestamp">Now.</param>
    /// <returns>How many steps to run now (0 to <see cref="MaxCatchUpSteps"/>).</returns>
    public int Advance(long timestamp)
    {
        long due = Math.Max(0, (timestamp - start) / stepTicks);
        long missing = due - stepsDone;
        if (missing <= 0)
        {
            return 0;
        }

        stepsDone = due;
        return (int)Math.Min(missing, MaxCatchUpSteps);
    }

    /// <summary>How far the moment is into the current step: Smooth Motion draws that share of the way from the previous step's positions to the current ones.</summary>
    /// <param name="timestamp">Now.</param>
    /// <returns>0 to 1.</returns>
    public float Progress(long timestamp) =>
        (float)Math.Clamp((double)(timestamp - start - stepsDone * stepTicks) / stepTicks, 0, 1);
}
