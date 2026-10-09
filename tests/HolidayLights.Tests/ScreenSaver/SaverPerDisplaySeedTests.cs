using System.Diagnostics;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.Core.Flash;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>PO decision 5 in the saver: identical displays never twinkle or random-flash in lockstep; the main display keeps the classic seed.</summary>
public sealed class SaverPerDisplaySeedTests
{
    private const uint Seed = 12345;

    [Theory]
    [InlineData(FlashPatternId.Twinkle)]
    [InlineData(FlashPatternId.RandomFlashing)]
    [InlineData(FlashPatternId.Combination)]
    public void IdenticalDisplays_FlashDifferently_WhileTheMainDisplayKeepsTheRunSeed(FlashPatternId pattern)
    {
        var settings = new AppSettings { Current = new ThemeableSettings { Flash = new FlashSettings { Pattern = pattern } } };
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit(settings);
            SaverOptions options = SaverOptions.FromSettings(settings, Seed);
            DisplayInfo main = SaverTestKit.Display(1920, 1080);
            DisplayInfo second = SaverTestKit.Display(1920, 1080, left: -1920, primary: false, number: 2);

            IReadOnlyList<SaverScene> both = kit.Services.CreateScenes(SaverDisplayPlan.For([main, second], SaverDisplays.All), options, null, Seed, 0);
            SaverScene alone = kit.Services.CreateScenes(SaverDisplayPlan.For([main], SaverDisplays.All), options, null, Seed, 0)[0];

            Assert.Equal(options.Flash, both[0].Options.Flash);
            Assert.NotEqual(options.Flash.PatternSeed, both[1].Options.Flash.PatternSeed);
            Assert.NotEqual(options.Flash.ClassicRandomSeed, both[1].Options.Flash.ClassicRandomSeed);

            // 300 samples over 3,300 ticks: at any Flash Interval that reaches Combination's Random Flashing and Twinkle segments.
            long tick = Stopwatch.Frequency * StepClock.TickMilliseconds / 1000;
            int samples = 0;
            int lockstep = 0;
            for (int i = 1; i <= 300; i++)
            {
                long timestamp = i * tick * 11 + tick / 2;
                foreach (SaverScene scene in (SaverScene[])[both[0], both[1], alone])
                {
                    scene.Bulbs!.Sample(timestamp);
                }

                Assert.Equal(alone.Bulbs!.States, both[0].Bulbs!.States);
                samples++;
                lockstep += both[0].Bulbs!.States.SequenceEqual(both[1].Bulbs!.States) ? 1 : 0;
            }

            // Combination also plays fixed patterns (Flash Together, Bulb Chase, ...), which are the same on identical displays.
            Assert.Equal(both[0].Bulbs!.States.Count, both[1].Bulbs!.States.Count);
            int allowed = pattern == FlashPatternId.Combination ? samples * 9 / 10 : samples / 10;
            Assert.True(lockstep < allowed, $"{lockstep} of {samples} samples were identical on both displays.");
        });
    }

    [Fact]
    public void ForDisplay_KeepsTheMainDisplayAndGivesEveryOtherDisplayItsOwnSeeds()
    {
        SaverOptions options = SaverOptions.FromSettings(new AppSettings(), Seed);

        Assert.Same(options, options.ForDisplay(0));
        FlashOptions[] others = [.. Enumerable.Range(1, 4).Select(i => options.ForDisplay(i).Flash)];
        Assert.Equal(5, others.Select(f => f.PatternSeed).Append(options.Flash.PatternSeed).Distinct().Count());
        Assert.Equal(5, others.Select(f => f.ClassicRandomSeed).Append(options.Flash.ClassicRandomSeed).Distinct().Count());
        Assert.All(others, f => Assert.Equal(options.Flash with { PatternSeed = f.PatternSeed, ClassicRandomSeed = f.ClassicRandomSeed }, f));
        Assert.Equal(options.ForDisplay(2), options.ForDisplay(2));
    }

    [Fact]
    public void CreateScenes_NumbersTheMainDisplayZero_WhereverItIsListed()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            SaverOptions options = SaverOptions.FromSettings(new AppSettings(), Seed);
            DisplayInfo left = SaverTestKit.Display(800, 600, left: -800, primary: false, number: 2);
            DisplayInfo main = SaverTestKit.Display(800, 600);

            IReadOnlyList<SaverScene> scenes = kit.Services.CreateScenes(
                [new SaverDisplayPlan(left, true, false), new SaverDisplayPlan(main, true, true)], options, null, Seed, 0);

            Assert.Equal(options.ForDisplay(1).Flash, scenes[0].Options.Flash);
            Assert.Equal(options.Flash, scenes[1].Options.Flash);
        });
    }
}
