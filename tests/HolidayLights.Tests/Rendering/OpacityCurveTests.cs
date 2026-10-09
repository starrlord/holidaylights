using System.Diagnostics;
using HolidayLights.Rendering.Composition;

namespace HolidayLights.Tests.Rendering;

public sealed class OpacityCurveTests
{
    private const long T0 = 1_000_000_000;

    private static long At(double milliseconds) => T0 + (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);

    [Fact]
    public void Ramp_ChangesLinearlyAndHoldsTheEndValue()
    {
        OpacityCurve ramp = OpacityCurves.Ramp(T0, 0.2, 1.0, 200);

        Assert.Equal(0.2, ramp.Evaluate(T0), 6);
        Assert.Equal(0.6, ramp.Evaluate(At(100)), 6);
        Assert.Equal(1.0, ramp.Evaluate(At(200)), 6);
        Assert.Equal(1.0, ramp.Evaluate(At(5_000)), 6);
        Assert.False(ramp.Repeats);
    }

    [Fact]
    public void Ramp_WithoutDurationIsAConstant()
    {
        Assert.True(OpacityCurves.Ramp(T0, 0, 0.5, 0).IsConstant);
        Assert.Equal(0.5f, OpacityCurves.Ramp(T0, 0, 0.5, 0).EndValue);
        Assert.True(OpacityCurves.Ramp(T0, 0.3, 0.3, 120).IsConstant);
    }

    [Fact]
    public void Transition_UsesTheTurnOnAndTurnOffDurationsOfTheProfile()
    {
        var profile = new FadeProfile(TurnOnMilliseconds: 72, TurnOffMilliseconds: 120);

        OpacityCurve on = OpacityCurves.Transition(T0, 0, 1, profile);
        OpacityCurve off = OpacityCurves.Transition(T0, 1, 0, profile);

        Assert.Equal(0.072, on.TerminalOffsetSeconds, 9);
        Assert.Equal(0.120, off.TerminalOffsetSeconds, 9);
        Assert.Equal(0.5, on.Evaluate(At(36)), 6);
        Assert.Equal(0.5, off.Evaluate(At(60)), 6);
        Assert.True(OpacityCurves.Transition(T0, 0, 1, FadeProfile.Instant).IsConstant);
    }

    [Fact]
    public void DelayedEaseOut_HoldsThenEasesOut()
    {
        OpacityCurve rise = OpacityCurves.DelayedEaseOut(T0, 0.4, 1.0, delayMilliseconds: 500, durationMilliseconds: 120);

        Assert.Equal(0.4, rise.Evaluate(At(250)), 6);
        Assert.Equal(0.4, rise.Evaluate(At(500)), 6);

        // Cubic ease-out: 1 - (1 - u)^3 at u = 0.5 is 0.875 of the change.
        Assert.Equal(0.4 + 0.6 * 0.875, rise.Evaluate(At(560)), 5);
        Assert.Equal(1.0, rise.Evaluate(At(620)), 6);
        Assert.Equal(1.0, rise.Evaluate(At(10_000)), 6);
    }

    [Theory]
    [InlineData(0, 0.0, 300)]
    [InlineData(5, 0.0, 300)]
    [InlineData(37, 0.0, 60)]
    [InlineData(12_345, 0.3125, 540)]
    public void RaisedCosine_FollowsTheWaveOfTheClock(long epochStep, double offset, double period)
    {
        var wave = new BrightnessWave(16, offset);
        var timing = new WaveTiming(T0, epochStep, period);
        OpacityCurve curve = OpacityCurves.RaisedCosine(wave, timing);

        Assert.True(curve.Repeats);
        for (double ms = 0; ms < 60 * period; ms += period / 7.3)
        {
            double x = epochStep + ms / period;
            Assert.InRange(curve.Evaluate(At(ms)) - wave.Evaluate(x), -0.01, 0.01);
        }
    }

    [Fact]
    public void RaisedCosine_IsContinuousAcrossASpeedChange()
    {
        var wave = new BrightnessWave(16, 0);
        var slow = new WaveTiming(T0, 0, 300);
        OpacityCurve before = OpacityCurves.RaisedCosine(wave, slow);

        // The speed changes at step 7 (2100 ms) to 60 ms per step.
        var fast = new WaveTiming(At(2_100), 7, 60);
        OpacityCurve after = OpacityCurves.RaisedCosine(wave, fast);

        Assert.InRange(before.Evaluate(At(2_100)) - after.Evaluate(At(2_100)), -0.01, 0.01);
        Assert.InRange(after.Evaluate(At(2_100 + 120)) - wave.Evaluate(9), -0.01, 0.01);
    }

    [Fact]
    public void RaisedCosineFrom_LeadsInWithoutAJumpAndThenFollowsTheWave()
    {
        var wave = new BrightnessWave(16, 0);
        var timing = new WaveTiming(T0, 0, 300);
        long now = At(1_000);
        OpacityCurve curve = OpacityCurves.RaisedCosineFrom(wave, timing, now, fromValue: 1.0, leadInMilliseconds: 600);

        Assert.Equal(1.0, curve.Evaluate(now), 6);
        for (double ms = 1_600; ms < 20_000; ms += 77)
        {
            Assert.InRange(curve.Evaluate(At(ms)) - wave.Evaluate(ms / 300), -0.01, 0.01);
        }

        // No jumps during the lead-in.
        double previous = curve.Evaluate(now);
        for (double ms = 1_000; ms <= 1_600; ms += 5)
        {
            double value = curve.Evaluate(At(ms));
            Assert.InRange(Math.Abs(value - previous), 0, 0.05);
            previous = value;
        }
    }

    [Fact]
    public void SquareWave_IsLitWhileTheWaveIsAtLeastOneHalf()
    {
        var wave = new BrightnessWave(16, 0);
        var timing = new WaveTiming(T0, 3, 300);
        OpacityCurve curve = OpacityCurves.SquareWave(wave, timing);

        for (double ms = 10; ms < 30_000; ms += 97)
        {
            double x = 3 + ms / 300;
            double expected = wave.Evaluate(x) >= 0.5 ? 1 : 0;
            double distanceToEdge = Math.Abs(wave.Evaluate(x) - 0.5);
            if (distanceToEdge > 0.01)
            {
                Assert.Equal(expected, curve.Evaluate(At(ms)), 6);
            }
        }
    }

    [Fact]
    public void DanceGroup_MatchesTheEnvelopeOfTheSequencerContract()
    {
        var update = new DanceGroupUpdate(Group: 4, StartBrightness: 0.2f, PeakBrightness: 0.9f, Timestamp: At(40));
        OpacityCurve curve = OpacityCurves.DanceGroup(update, now: T0, valueNow: 0.25);

        Assert.Equal(0.25, curve.Evaluate(T0), 6);
        Assert.Equal(0.2, curve.Evaluate(At(40)), 4);
        for (double ms = 41; ms < 1_500; ms += 13)
        {
            double expected = DanceEnvelope.BrightnessAt(0.2, 0.9, ms - 40);
            Assert.InRange(curve.Evaluate(At(ms)) - expected, -0.005, 0.005);
        }

        Assert.Equal(0, curve.Evaluate(At(5_000)), 6);
    }

    [Fact]
    public void DanceGroup_ForALateEventStartsAtTheEvent()
    {
        var update = new DanceGroupUpdate(0, 0, 1, Timestamp: T0);
        OpacityCurve curve = OpacityCurves.DanceGroup(update, now: At(100), valueNow: 0.5);

        Assert.Equal(T0, curve.BeginTimestamp);
        Assert.InRange(curve.Evaluate(At(130)) - DanceEnvelope.BrightnessAt(0, 1, 130), -0.005, 0.005);
    }

    [Fact]
    public void Scale_MultipliesEveryValue()
    {
        OpacityCurve ramp = OpacityCurves.Ramp(T0, 0, 1, 100);
        OpacityCurve glow = ramp.Scale(0.55f);

        Assert.Equal(0.275, glow.Evaluate(At(50)), 5);
        Assert.Equal(0.55, glow.Evaluate(At(500)), 5);
        Assert.Same(ramp, ramp.Scale(1f));
        Assert.Equal(0.275f, OpacityCurve.Constant(0.5f).Scale(0.55f).EndValue, 5);
    }

    [Fact]
    public void EnteredAt_ReexpandsThePolynomial()
    {
        var segment = new CubicSegment(0.1, 0.2, -0.7, 1.3, 0.9);
        CubicSegment entered = segment.EnteredAt(0.25, 0);

        for (double u = 0; u < 0.5; u += 0.05)
        {
            Assert.Equal(segment.Evaluate(u + 0.25), entered.Evaluate(u), 9);
        }
    }

    public static TheoryData<string> Curves => ["ramp", "rise", "wave", "lead-in", "square", "dance"];

    [Theory]
    [MemberData(nameof(Curves))]
    public void StartingAt_KeepsTheFunction(string kind)
    {
        OpacityCurve curve = Make(kind);
        foreach (double shift in new[] { -250.0, 0, 3, 47.5, 199, 700, 1_234.5, 9_876 })
        {
            long newBegin = At(shift);
            OpacityCurve moved = curve.StartingAt(newBegin);
            if (!moved.IsConstant)
            {
                Assert.Equal(newBegin, moved.BeginTimestamp);
            }

            for (double ms = Math.Max(0, shift); ms < shift + 25_000; ms += 31.7)
            {
                Assert.InRange(moved.Evaluate(At(ms)) - curve.Evaluate(At(ms)), -1e-6, 1e-6);
            }
        }
    }

    [Fact]
    public void StartingAt_AfterTheEndOfAFiniteCurveIsItsEndValue()
    {
        OpacityCurve moved = OpacityCurves.Ramp(T0, 1, 0.25, 100).StartingAt(At(500));

        Assert.True(moved.IsConstant);
        Assert.Equal(0.25f, moved.EndValue);
    }

    [Fact]
    public void StartingAt_InsideTheRepeatRotatesOnePeriod()
    {
        OpacityCurve wave = OpacityCurves.RaisedCosine(new BrightnessWave(16, 0), new WaveTiming(T0, 0, 300));
        OpacityCurve moved = wave.StartingAt(At(4_800 * 3 + 1_000));

        Assert.True(moved.Repeats);
        Assert.Equal(4.8, moved.RepeatSeconds, 9);
        Assert.Equal(moved.TerminalOffsetSeconds, moved.RepeatSeconds, 9);
        Assert.Equal(0, moved.Segments[0].OffsetSeconds);
    }

    private static OpacityCurve Make(string kind) => kind switch
    {
        "ramp" => OpacityCurves.Ramp(T0, 0.1, 0.9, 400),
        "rise" => OpacityCurves.DelayedEaseOut(T0, 0, 1, 900, 120),
        "wave" => OpacityCurves.RaisedCosine(new BrightnessWave(16, 0.25), new WaveTiming(T0, 21, 180)),
        "lead-in" => OpacityCurves.RaisedCosineFrom(new BrightnessWave(16, 0), new WaveTiming(T0 - Stopwatch.Frequency, 0, 300), T0, 1, 600),
        "square" => OpacityCurves.SquareWave(new BrightnessWave(16, 0), new WaveTiming(T0, 5, 120)),
        _ => OpacityCurves.DanceGroup(new DanceGroupUpdate(1, 0.1f, 0.8f, At(30)), T0, 0.3),
    };
}
