using System.Diagnostics;
using HolidayLights.Tests.Layout;

namespace HolidayLights.Tests.Flash;

/// <summary>
/// The per-step budget: evaluating 2,000 bulbs takes less than 0.2 ms for every pattern, without allocating (the Lights
/// thread steps at up to 16 Hz; previews sample at up to 30 fps).
/// </summary>
public sealed class PerformanceTests
{
    private const double BudgetMilliseconds = 0.2;
    private const int Bulbs = 2000;

    private static (LightsLayout Layout, IBulbResolver Bulbs) BigLayout()
    {
        string Id(string slug) => BulbIds.BuiltIn(slug);
        var arrangement = new SlotAssignment
        {
            Top = [Id("mini-bulbs"), Id("candy-canes")],
            Bottom = [Id("mini-bulbs"), Id("jolly-holly")],
            Right = [Id("mini-bulbs"), Id("carolers")],
            Left = [Id("standard-bulbs"), Id("mini-bulbs")],
            TopLeft = Id("standard-bulbs"), TopRight = Id("candy-canes"), BottomLeft = Id("jolly-holly"), BottomRight = Id("standard-bulbs"),
        };
        FakeBulbResolver bulbs = TableBulbs.Resolver;
        LayoutTarget[] targets =
        [
            new("1", new RectI(0, 0, 3840, 2088), 1.0),
            new("2", new RectI(-3840, 0, 0, 2088), 1.0),
            new("3", new RectI(3840, 0, 7680, 2088), 1.0),
            new("4", new RectI(-7680, 0, -3840, 2088), 1.0),
        ];
        LightsLayout layout = FlashTestKit.LayoutEngine.Layout(targets, arrangement, bulbs);
        Assert.True(layout.Placements.Count >= Bulbs, $"{layout.Placements.Count} bulbs");
        return (layout, bulbs);
    }

    public static TheoryData<FlashPatternId> Patterns => [.. Enum.GetValues<FlashPatternId>()];

    [Theory]
    [MemberData(nameof(Patterns))]
    public void MoveTo_EvaluatesTwoThousandBulbsWithinTheBudget(FlashPatternId pattern)
    {
        var frame = BigLayout();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = pattern, PatternSeed = 77 });
        if (pattern == FlashPatternId.DanceToMusic)
        {
            sequencer.ApplyMusicEvents([new MusicEvent(MusicEventKind.SongStarted, 0, 0, 0, 0), new MusicEvent(MusicEventKind.NoteOn, 0, 0, 60, 100)]);
            Assert.True(sequencer.IsDancing);
        }

        long step = 0;
        double perStep = Measure(() => sequencer.MoveTo(++step, FlashTestKit.Ms(step * 60)));
        double perTwoThousand = perStep * Bulbs / frame.Layout.Placements.Count;
        Assert.True(perTwoThousand < BudgetMilliseconds, $"{pattern}: {perTwoThousand:F4} ms per step for {Bulbs} bulbs");
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void MoveToAndSample_DoNotAllocate(FlashPatternId pattern)
    {
        var frame = BigLayout();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = pattern, PatternSeed = 5 });
        var states = new BulbVisualState[frame.Layout.Placements.Count];
        StepClock clock = StepClock.Start(0, 2);
        for (int step = 1; step < 50; step++)
        {
            sequencer.MoveTo(step, 0);
            sequencer.Sample(FlashTestKit.Ms(step * 120), clock, states);
        }

        // The fewest bytes of several batches: an allocation per step shows in every batch, while one-time work of the
        // runtime on this thread (tier-up, first-use initialization while the whole suite runs) shows in one at most.
        long fewest = long.MaxValue;
        for (int batch = 0, step = 50; batch < 3; batch++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int end = step + 400; step < end; step++)
            {
                sequencer.MoveTo(step, FlashTestKit.Ms(step * 120));
                sequencer.Sample(FlashTestKit.Ms((step * 120) + 37), clock, states);
                _ = sequencer.GetWave(step % states.Length);
                _ = sequencer.GetFadeProfile(120);
            }

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, fewest);
    }

    [Theory]
    [InlineData(FlashPatternId.FlashTogether)]
    [InlineData(FlashPatternId.Twinkle)]
    [InlineData(FlashPatternId.Waves)]
    [InlineData(FlashPatternId.DanceToMusic)]
    public void Sample_ResolvesTwoThousandBulbsQuickly(FlashPatternId pattern)
    {
        var frame = BigLayout();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = pattern });
        var states = new BulbVisualState[frame.Layout.Placements.Count];
        StepClock clock = StepClock.Start(0, 5);
        long t = 0;
        double perSample = Measure(() => sequencer.Sample(t += FlashTestKit.Ms(33), clock, states));
        double perTwoThousand = perSample * Bulbs / frame.Layout.Placements.Count;
        Assert.True(perTwoThousand < 2 * BudgetMilliseconds, $"{pattern}: {perTwoThousand:F4} ms per sample for {Bulbs} bulbs");
    }

    // The best of several batches, so that other tests running in parallel do not distort the result.
    private static double Measure(Action action)
    {
        for (int i = 0; i < 100; i++)
        {
            action();
        }

        double best = double.MaxValue;
        for (int batch = 0; batch < 7; batch++)
        {
            const int Runs = 150;
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Runs; i++)
            {
                action();
            }

            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds / Runs);
        }

        return best;
    }
}
