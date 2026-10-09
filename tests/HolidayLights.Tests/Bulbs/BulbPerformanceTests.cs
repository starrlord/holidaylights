using System.Diagnostics;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

/// <summary>Timing tests run alone so other tests do not compete for the CPU.</summary>
[CollectionDefinition(nameof(BulbPerformanceCollection), DisableParallelization = true)]
public sealed class BulbPerformanceCollection;

[Collection(nameof(BulbPerformanceCollection))]
public sealed class BulbPerformanceTests
{
    [Fact]
    public void IndexingAllBundledBulbs_TakesLessThanOneAndAHalfSeconds()
    {
        using var harness = new CatalogHarness();

        var clock = Stopwatch.StartNew();
        harness.Start();
        TimeSpan cold = clock.Elapsed;
        harness.Restart();
        clock.Restart();
        harness.Start();
        TimeSpan warm = clock.Elapsed;

        Assert.Equal(1550, harness.Catalog.All.Count);
        Assert.True(cold < PerformanceBudget.Of(TimeSpan.FromSeconds(1.5)), $"Indexing without a cache took {cold.TotalMilliseconds:F0} ms.");
        Assert.True(warm < PerformanceBudget.Of(TimeSpan.FromSeconds(1)), $"Indexing from the cache took {warm.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public void DecodingATypicalBulb_TakesLessThan20Milliseconds()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(BulbGoldens.BundledBulbsFolder, "10thBirthday.bul"));
        BulFile.Parse(data).Entries.Where(e => e.Size > 0).ToList().ForEach(e => GifDecoder.DecodeClassic(e.Gif.Span));

        var times = new List<double>();
        for (int run = 0; run < 7; run++)
        {
            var clock = Stopwatch.StartNew();
            IBulb bulb = AddOnBulbs.FromFile(BulFile.Parse(data), "addon:10thBirthday", BulbOrigin.BundledAddOn, $"perf-{Guid.NewGuid():N}");
            foreach (CellSlot slot in Enum.GetValues<CellSlot>())
            {
                int flavors = slot.IsSide() ? bulb.GetFlavorCount(slot.ToSide()) : 1;
                for (int flavor = 0; flavor < flavors; flavor++)
                {
                    bulb.GetCell(slot, flavor, 0);
                }
            }

            times.Add(clock.Elapsed.TotalMilliseconds);
        }

        double median = times.Order().ElementAt(times.Count / 2);
        Assert.True(median < PerformanceBudget.Milliseconds(20), $"Decoding every animation of a typical bulb took {median:F1} ms.");
    }
}
