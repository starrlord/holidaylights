using System.Diagnostics;
using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Engine;
using HolidayLights.Tests.Rendering.Fakes;

namespace HolidayLights.Tests.Rendering;

public sealed class PowerUpAndClockTests
{
    private const long T0 = 9_000_000_000;

    private static long At(double milliseconds) => T0 + (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);

    [Fact]
    public void FirstRunPowerUp_RunsTheWaveAroundEachRingAndHoldsBeforeThePattern()
    {
        LightsLayout layout = SyntheticScene.Layout();
        PowerUpPlan plan = PowerUpPlan.Create(SceneTransition.FirstRunPowerUp, reducedMotion: false, layout);

        Assert.True(plan.HasWave);
        Assert.Equal(200, plan.SceneFadeInMilliseconds);
        Assert.Equal(2_200, plan.PatternStartMilliseconds);
        LightsRing ring = Assert.Single(layout.Rings);
        int count = ring.Ordinals.Count;
        for (int k = 0; k < count; k++)
        {
            Assert.Equal(300 + 1_600.0 * k / count, plan.FullBrightnessAt(ring.Ordinals[k]), 9);
        }

        Assert.True(plan.FullBrightnessAt(ring.Ordinals[^1]) < 1_900);
    }

    [Theory]
    [InlineData(SceneTransition.AutostartPowerUp, 1_300)]
    [InlineData(SceneTransition.ShortPowerUp, 1_100)]
    [InlineData(SceneTransition.ThemeTransition, 1_100)]
    public void ShortPowerUps_HaveNoHold(SceneTransition transition, double patternStart)
    {
        PowerUpPlan plan = PowerUpPlan.Create(transition, reducedMotion: false, SyntheticScene.Layout());

        Assert.Equal(patternStart, plan.PatternStartMilliseconds);
        Assert.Equal(300, plan.FullBrightnessAt(SyntheticScene.Layout().Rings[0].Ordinals[0]));
    }

    [Fact]
    public void ReducedMotion_OnlyFadesEverythingInTogether()
    {
        PowerUpPlan plan = PowerUpPlan.Create(SceneTransition.FirstRunPowerUp, reducedMotion: true, SyntheticScene.Layout());

        Assert.False(plan.HasWave);
        Assert.Equal(300, plan.SceneFadeInMilliseconds);
        Assert.Equal(0, plan.PatternStartMilliseconds);
    }

    [Theory]
    [InlineData(SceneTransition.FirstRunPowerUp, true)]
    [InlineData(SceneTransition.AutostartPowerUp, true)]
    [InlineData(SceneTransition.ShortPowerUp, true)]
    [InlineData(SceneTransition.ThemeTransition, true)]
    [InlineData(SceneTransition.Automatic, false)]
    [InlineData(SceneTransition.None, false)]
    public void IsPowerUp_ClassifiesTransitions(SceneTransition transition, bool expected) =>
        Assert.Equal(expected, PowerUpPlan.IsPowerUp(transition));

    [Fact]
    public void Clock_RestartsAtStepZeroAndStepsOnTheTickGrid()
    {
        var keeper = new ClockKeeper(T0, 5);
        Assert.False(keeper.IsRunning);
        Assert.Null(keeper.DueStep(At(1_000)));

        keeper.Restart(T0, 5);
        Assert.Equal(0, keeper.DueStep(T0));
        keeper.MarkShown(0);
        Assert.Null(keeper.DueStep(At(299)));
        Assert.Equal(At(300), keeper.NextDue(steps: true));
        Assert.Equal(1, keeper.DueStep(At(300)));
        Assert.Equal(1, keeper.DueStep(At(299.9)));
        Assert.Equal(3, keeper.DueStep(At(1_000)));
        Assert.Null(keeper.NextDue(steps: false));
    }

    [Fact]
    public void Clock_ChangesSpeedAtTheNextBoundaryWithoutResettingTheStep()
    {
        var keeper = new ClockKeeper(T0, 5);
        keeper.Restart(T0, 5);
        keeper.MarkShown(3);

        Assert.True(keeper.ChangeInterval(1, At(1_000)));
        Assert.False(keeper.ChangeInterval(1, At(1_010)));
        Assert.Equal(At(1_200), keeper.SpeedChangeAt);
        Assert.Equal(4, keeper.Clock.StepAt(At(1_200)));
        Assert.Equal(5, keeper.Clock.StepAt(At(1_260)));
        Assert.Null(keeper.TakeSpeedChange(At(1_100)));
        Assert.Equal(At(1_200), keeper.TakeSpeedChange(At(1_199.9)));
        Assert.Null(keeper.SpeedChangeAt);
        Assert.Null(keeper.TakeSpeedChange(At(1_300)));
        Assert.Equal(60, keeper.TimingAt(At(1_300)).StepPeriodMilliseconds);
        Assert.Equal(300, keeper.TimingAt(At(1_100)).StepPeriodMilliseconds);
    }

    [Fact]
    public void Clock_FreezesWhileEverythingRestsAndContinuesFromTheShownStep()
    {
        var keeper = new ClockKeeper(T0, 5);
        keeper.Restart(T0, 5);
        keeper.MarkShown(7);
        keeper.Freeze(At(2_150));
        Assert.Null(keeper.DueStep(At(60_000)));

        Assert.True(keeper.Resume(At(60_000)));
        Assert.False(keeper.Resume(At(60_001)));
        Assert.Equal(7, keeper.Clock.StepAt(At(60_000)));
        Assert.Equal(8, keeper.DueStep(At(60_300)));
    }

    [Fact]
    public void Clock_FrozenWithoutProcessedSteps_KeepsTheStepItShowed()
    {
        // Repeating animations showed steps 1-9 without the thread processing them.
        var keeper = new ClockKeeper(T0, 5);
        keeper.Restart(T0, 5);
        keeper.MarkShown(0);
        keeper.Freeze(At(2_950));

        Assert.True(keeper.Resume(At(60_000)));
        Assert.Equal(9, keeper.Clock.StepAt(At(60_000)));
        Assert.Equal(10, keeper.DueStep(At(60_300)));
    }

    [Fact]
    public void Clock_StartsThePatternWhenThePowerUpEnds()
    {
        var keeper = new ClockKeeper(T0, 5);
        keeper.StartLater(At(2_200));

        Assert.Equal(At(2_200), keeper.NextDue(steps: true));
        Assert.False(keeper.StartDue(At(2_000)));
        Assert.True(keeper.StartDue(At(2_200)));
        Assert.False(keeper.Resume(At(2_100)));
    }

    [Fact]
    public void Clock_FrozenDuringAPowerUpStartsAtStepZeroOnResume()
    {
        var keeper = new ClockKeeper(T0, 5);
        keeper.StartLater(At(2_200));
        keeper.Freeze(At(1_000));

        Assert.Null(keeper.StartAt);
        Assert.True(keeper.Resume(At(10_000)));
        Assert.Equal(0, keeper.DueStep(At(10_000)));
    }

    [Fact]
    public void SceneRequests_KeepAnAnimatedTransitionAgainstALaterEdit()
    {
        LightsScene first = FakeLayout.Scene([SyntheticScene.Display], SyntheticScene.Layout(), LayerMode.BehindIcons);
        LightsScene second = first with { Revision = 2 };

        SceneRequest merged = SceneRequest.Merge(new SceneRequest(first, SceneTransition.ThemeTransition), new SceneRequest(second, SceneTransition.Automatic));
        Assert.Same(second, merged.Scene);
        Assert.Equal(SceneTransition.ThemeTransition, merged.Transition);

        Assert.Equal(SceneTransition.None, SceneRequest.Merge(new SceneRequest(first, SceneTransition.ShortPowerUp), new SceneRequest(second, SceneTransition.None)).Transition);
        Assert.Equal(SceneTransition.Automatic, SceneRequest.Merge(null, new SceneRequest(second, SceneTransition.Automatic)).Transition);
    }
}
