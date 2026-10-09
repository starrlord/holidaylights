using HolidayLights.Core.Flash;

namespace HolidayLights.Tests.Flash;

/// <summary>The timing rules (PRODUCT-SPEC 5.5).</summary>
public sealed class FlashClockTests
{
    [Theory]
    [InlineData(FlashPatternId.FlashTogether, 5, 5, 300)]
    [InlineData(FlashPatternId.BulbChase, 1, 1, 60)]
    [InlineData(FlashPatternId.Twinkle, 9, 9, 540)]
    [InlineData(FlashPatternId.DontFlash, 5, 10, 600)]
    [InlineData(FlashPatternId.DontFlash, 1, 10, 600)]
    [InlineData(FlashPatternId.Alternating, 0, 1, 60)]
    [InlineData(FlashPatternId.Alternating, 12, 9, 540)]
    public void TicksPerStep_IsTheIntervalAndTenForDontFlash(FlashPatternId pattern, int interval, int ticks, int period)
    {
        Assert.Equal(60, FlashClock.TickMilliseconds);
        Assert.Equal(ticks, FlashClock.TicksPerStep(pattern, interval));
        Assert.Equal(period, FlashClock.StepPeriodMilliseconds(pattern, interval));
    }

    [Theory]
    [InlineData(1, true, 3)]
    [InlineData(2, true, 3)]
    [InlineData(3, true, 3)]
    [InlineData(7, true, 7)]
    [InlineData(1, false, 1)]
    [InlineData(0, false, 1)]
    public void LimitInterval_KeepsTheLightsAtThreeTicksOrSlower(int interval, bool limited, int expected) =>
        Assert.Equal(expected, FlashClock.LimitInterval(interval, limited));

    [Fact]
    public void LimitedLights_CompleteFewerThanThreeFlashesPerSecond()
    {
        // A light bulb flashing every step completes one on/off cycle in two steps.
        double cyclesPerSecond = 1000.0 / (2 * FlashClock.StepPeriodMilliseconds(FlashPatternId.FlashTogether, FlashClock.LimitInterval(1, true)));
        Assert.True(cyclesPerSecond <= 3, $"{cyclesPerSecond} cycles per second");
        Assert.Equal(1000.0 / 3, FlashClock.LimitedRetriggerMilliseconds);
    }

    [Fact]
    public void RunsClock_IsFalseForStaticPictures()
    {
        Assert.True(FlashClock.RunsClock(new FlashOptions { Pattern = FlashPatternId.FlashTogether }));
        Assert.True(FlashClock.RunsClock(new FlashOptions { Pattern = FlashPatternId.DanceToMusic }));
        Assert.False(FlashClock.RunsClock(new FlashOptions { Pattern = FlashPatternId.DontFlash }));
        Assert.False(FlashClock.RunsClock(new FlashOptions { Pattern = FlashPatternId.Twinkle, StopFlashing = true }));
        Assert.Throws<ArgumentNullException>(() => FlashClock.RunsClock(null!));
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(9, 4, 1)]
    [InlineData(-1, 4, 3)]
    [InlineData(123456789, 8, 5)]
    [InlineData(7, 1, 0)]
    public void StripFrame_IsTheStepModuloTheStripFrames(long step, int frames, int expected) =>
        Assert.Equal(expected, FlashClock.StripFrame(step, frames));

    [Fact]
    public void StripFrame_RejectsStripsWithoutFrames() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FlashClock.StripFrame(3, 0));
}
