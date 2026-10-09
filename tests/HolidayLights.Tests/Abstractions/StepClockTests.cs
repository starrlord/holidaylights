using System.Diagnostics;

namespace HolidayLights.Tests.Abstractions;

public sealed class StepClockTests
{
    private static long Ms(double milliseconds) => (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0);

    [Fact]
    public void Start_CountsStepsOfIntervalTimesSixtyMilliseconds()
    {
        StepClock clock = StepClock.Start(timestamp: 1_000_000, interval: 5);

        Assert.Equal(300, clock.StepPeriodMilliseconds);
        Assert.Equal(0, clock.StepAt(1_000_000));
        Assert.Equal(0, clock.StepAt(1_000_000 + Ms(299)));
        Assert.Equal(1, clock.StepAt(1_000_000 + Ms(300)));
        Assert.Equal(10, clock.StepAt(1_000_000 + Ms(3_000)));
        Assert.Equal(2.5, clock.PositionAt(1_000_000 + Ms(750)), 6);
        Assert.Equal(1_000_000 + Ms(900), clock.TimestampOfStep(3));
    }

    [Fact]
    public void WithInterval_ChangesSpeedAtTheNextBoundaryWithoutResettingTheStep()
    {
        long t0 = 5_000_000;
        StepClock clock = StepClock.Start(t0, interval: 5);

        // Requested in the middle of step 3 (900..1200 ms): the new speed starts at step 4 (1200 ms).
        StepClock faster = clock.WithInterval(1, t0 + Ms(1_000));

        Assert.Equal(4, faster.EpochStep);
        Assert.Equal(t0 + Ms(1_200), faster.EpochTimestamp);
        Assert.Equal(3, faster.StepAt(t0 + Ms(1_100)));
        Assert.Equal(3.5, faster.PositionAt(t0 + Ms(1_050)), 6);
        Assert.Equal(4, faster.StepAt(t0 + Ms(1_200)));
        Assert.Equal(5, faster.StepAt(t0 + Ms(1_260)));
        Assert.Equal(t0 + Ms(900), faster.TimestampOfStep(3));
    }

    [Fact]
    public void WithInterval_ReplacesAPendingChange()
    {
        long t0 = 0;
        StepClock clock = StepClock.Start(t0, interval: 5).WithInterval(1, t0 + Ms(100));

        StepClock again = clock.WithInterval(9, t0 + Ms(200));

        Assert.Equal(1, again.EpochStep);
        Assert.Equal(9, again.Interval);
        Assert.Equal(0, again.StepAt(t0 + Ms(250)));
        Assert.Equal(1, again.StepAt(t0 + Ms(300 + 539)));
        Assert.Equal(2, again.StepAt(t0 + Ms(300 + 540)));
    }

    [Fact]
    public void Start_RejectsIntervalsOutsideOneToNine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StepClock.Start(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StepClock.Start(0, 10));
    }
}
