using System.Diagnostics;
using HolidayLights.Rendering.Composition;

namespace HolidayLights.Rendering.Engine;

/// <summary>
/// The presenter's step clock (PRODUCT-SPEC 5.5.1, CONTRACTS 5 "StepClock is the one step clock"): a pattern starts at step 0
/// (after a power-up when there is one), a speed change takes effect at the next step boundary without resetting the step,
/// and the clock stops while every light rests and continues from the shown step when they come back.
/// </summary>
internal sealed class ClockKeeper
{
    /// <summary>Wake-ups this close before a boundary already process it (timer granularity).</summary>
    private static readonly long Tolerance = Stopwatch.Frequency / 4000;

    /// <summary>Creates a stopped clock.</summary>
    public ClockKeeper(long now, int interval)
    {
        Interval = interval;
        Clock = StepClock.Start(now, interval);
    }

    /// <summary>The published clock.</summary>
    public StepClock Clock { get; private set; }

    /// <summary>The interval the clock runs (or will run) at.</summary>
    public int Interval { get; private set; }

    /// <summary>True while steps are processed.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The last processed step (-1 before the first).</summary>
    public long LastStep { get; private set; } = -1;

    /// <summary>When the pattern starts after a power-up, or null.</summary>
    public long? StartAt { get; private set; }

    /// <summary>When a pending speed change takes effect (waves must be re-anchored then), or null.</summary>
    public long? SpeedChangeAt { get; private set; }

    /// <summary>The wave timing at a moment.</summary>
    public WaveTiming TimingAt(long timestamp) => WaveTiming.At(Clock, timestamp);

    /// <summary>Starts the pattern at step 0 at <paramref name="timestamp"/>.</summary>
    public void Restart(long timestamp, int interval)
    {
        Interval = interval;
        Clock = StepClock.Start(timestamp, interval);
        IsRunning = true;
        LastStep = -1;
        StartAt = null;
        SpeedChangeAt = null;
    }

    /// <summary>Stops stepping until <paramref name="timestamp"/>, when the pattern starts at step 0 (the end of a power-up).</summary>
    public void StartLater(long timestamp)
    {
        IsRunning = false;
        StartAt = timestamp;
        SpeedChangeAt = null;
    }

    /// <summary>Changes the speed at the next boundary (or for the next start).</summary>
    /// <returns>True when the published clock changed.</returns>
    public bool ChangeInterval(int interval, long now)
    {
        if (interval == Interval)
        {
            return false;
        }

        Interval = interval;
        if (!IsRunning)
        {
            return false;
        }

        Clock = Clock.WithInterval(interval, now);
        SpeedChangeAt = Clock.EpochTimestamp > now ? Clock.EpochTimestamp : null;
        return true;
    }

    /// <summary>
    /// Stops the clock (everything rests). The step showing at <paramref name="now"/> is kept (steps are not processed when
    /// repeating animations show them); a pending power-up start becomes a start at step 0 on resume.
    /// </summary>
    public void Freeze(long now)
    {
        if (StartAt is not null)
        {
            StartAt = null;
            LastStep = -1;
        }
        else if (IsRunning)
        {
            LastStep = Math.Max(LastStep, Clock.StepAt(now));
        }

        IsRunning = false;
        SpeedChangeAt = null;
    }

    /// <summary>Continues after a rest: the shown step goes on from <paramref name="now"/>.</summary>
    /// <returns>True when the clock was restarted.</returns>
    public bool Resume(long now)
    {
        if (IsRunning || StartAt is not null)
        {
            return false;
        }

        Clock = StepClock.Start(now, Interval, Math.Max(0, LastStep));
        IsRunning = true;
        return true;
    }

    /// <summary>The next moment the clock needs the thread, or null.</summary>
    public long? NextDue(bool steps)
    {
        long? next = StartAt;
        if (IsRunning && steps)
        {
            long boundary = Clock.TimestampOfStep(LastStep + 1);
            next = next is { } start ? Math.Min(start, boundary) : boundary;
        }

        if (SpeedChangeAt is { } change)
        {
            next = next is { } pending ? Math.Min(pending, change) : change;
        }

        return next;
    }

    /// <summary>True when the delayed start is due.</summary>
    public bool StartDue(long now) => StartAt is { } start && now + Tolerance >= start;

    /// <summary>When a speed change that has just taken effect began (clears it), or null.</summary>
    public long? TakeSpeedChange(long now)
    {
        if (SpeedChangeAt is { } change && now + Tolerance >= change)
        {
            SpeedChangeAt = null;
            return change;
        }

        return null;
    }

    /// <summary>The step to show now when it is newer than the last one, else null.</summary>
    public long? DueStep(long now)
    {
        if (!IsRunning)
        {
            return null;
        }

        long step = Clock.StepAt(now + Tolerance);
        return step > LastStep ? step : null;
    }

    /// <summary>Records the shown step.</summary>
    public void MarkShown(long step) => LastStep = step;
}
