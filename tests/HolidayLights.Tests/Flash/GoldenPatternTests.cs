using System.Text.Json;
using HolidayLights.Core.Flash;
using HolidayLights.Tests.Layout;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Flash;

/// <summary>
/// The five classic patterns against the golden layouts (PRODUCT-SPEC 5.6, 7.5 #7): for every golden case the strip frame
/// counts W and the phase of every placement in world frames 0..min(W, 8) - 1, including Random Flashing with
/// <c>srand(1)</c>; and the MSVC <c>rand()</c> sequences of <c>msvc-rand.json</c>.
/// </summary>
public sealed class GoldenPatternTests
{
    [Theory]
    [MemberData(nameof(GoldenLayouts.Files), MemberType = typeof(GoldenLayouts))]
    public void ClassicPatterns_EqualLayoutSimPhases(string fileName)
    {
        GoldenTheme theme = GoldenLayouts.Read(fileName);
        FakeBulbResolver bulbs = TableBulbs.Resolver;
        foreach (GoldenCase golden in theme.Cases)
        {
            LightsLayout layout = FlashTestKit.LayoutEngine.Layout([new LayoutTarget("display", golden.Rect, 1.0)], theme.Arrangement, bulbs);
            IFlashSequencer sequencer = FlashTestKit.Engine.CreateSequencer(layout, bulbs, new FlashOptions
            {
                Pattern = golden.Pattern,
                ClassicRandomSeed = golden.Seed ?? 1,
            });
            IReadOnlyList<StripLayout> strips = layout.Displays[0].Strips;
            for (int step = 0; step < RandomFlashingTable.FramesPerStrip; step++)
            {
                sequencer.MoveTo(step, step);
                for (int s = 0; s < strips.Count; s++)
                {
                    GoldenStrip expected = golden.Strips[s];
                    for (int p = 0; p < expected.Placements.Count; p++)
                    {
                        int ordinal = strips[s].Placements[p].Ordinal;
                        Assert.True(expected.Frames == sequencer.GetBulb(ordinal).StripFrameCount, $"{fileName} {golden.Rect} pattern {(int)golden.Pattern} {expected.Side}: W");
                        IReadOnlyList<int> phases = expected.Placements[p].Phases;
                        if (step < phases.Count)
                        {
                            Assert.True(
                                phases[step] == sequencer.Current[ordinal].Frame,
                                $"{fileName} {golden.Rect} pattern {(int)golden.Pattern} {expected.Side} placement {p} frame {step}: {sequencer.Current[ordinal].Frame}, expected {phases[step]}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void MsvcRandom_EqualsTheCrtSequences()
    {
        using JsonDocument golden = GoldenData.ReadJson("msvc-rand.json");
        JsonElement values = golden.RootElement.GetProperty("values");
        int seeds = 0;
        foreach (JsonProperty seed in values.EnumerateObject())
        {
            var random = new MsvcRandom(uint.Parse(seed.Name));
            int[] expected = [.. seed.Value.EnumerateArray().Select(v => v.GetInt32())];
            Assert.Equal(2000, expected.Length);
            Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => random.Next()));
            seeds++;
        }

        Assert.Equal(5, seeds);
    }

    [Fact]
    public void MsvcRandom_SrandOneStartsWithTheWellKnownValues()
    {
        var random = new MsvcRandom(1);
        Assert.Equal([41, 18467, 6334, 26500, 19169], Enumerable.Range(0, 5).Select(_ => random.Next()));
    }
}
