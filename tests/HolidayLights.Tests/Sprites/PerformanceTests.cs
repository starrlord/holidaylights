using System.Diagnostics;
using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Sprites;

/// <summary>Timing tests run alone, so other tests running in parallel do not distort the measurement.</summary>
[CollectionDefinition(nameof(SpritePerformanceCollection), DisableParallelization = true)]
public sealed class SpritePerformanceCollection;

/// <summary>Speed of the sprite pipeline on the 49 built-in bulbs (every distinct cell, 890 in all).</summary>
[Collection(nameof(SpritePerformanceCollection))]
public sealed class PerformanceTests(ITestOutputHelper output)
{
    private static readonly double BudgetMilliseconds = PerformanceBudget.Milliseconds(300);

    [Theory]
    [InlineData(SpriteStyle.Smooth)]
    [InlineData(SpriteStyle.Crisp)]
    public void ScalingEveryBuiltInCellAt150PercentTakesUnder300Milliseconds(SpriteStyle style)
    {
        Rgba32Image[] cells = [.. BuiltInTestBulbs.Instance.All.SelectMany(b => b.Cells)];
        Assert.Equal(890, cells.Length);
        PixelArtScaler.Scale(cells[0], 1.5, style);

        double best = double.MaxValue;
        long pixels = 0;
        for (int run = 0; run < 3 && best >= BudgetMilliseconds; run++)
        {
            pixels = 0;
            long start = Stopwatch.GetTimestamp();
            foreach (Rgba32Image cell in cells)
            {
                pixels += PixelArtScaler.Scale(cell, 1.5, style).Pixels.Length;
            }

            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        output.WriteLine($"{style}: {cells.Length} cells, {pixels:N0} output pixels in {best:F1} ms");
        Assert.True(best < BudgetMilliseconds, $"Scaling took {best:F1} ms.");
    }
}
