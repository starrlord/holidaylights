using System.Globalization;
using System.Text.Json;
using HolidayLights.Audio.Scheduling;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Audio;

/// <summary>The shuffle bag against golden <c>shuffle.json</c> with 5.4's MSVC rand().</summary>
public sealed class ShuffleBagGoldenTests
{
    public static TheoryData<int> SequenceIndexes() => Indexes("sequences");

    public static TheoryData<int> ScenarioIndexes() => Indexes("scenarios");

    [Fact]
    public void Msvc_rand_matches_golden()
    {
        using JsonDocument golden = GoldenData.ReadJson("msvc-rand.json");
        JsonProperty[] seeds = [.. golden.RootElement.GetProperty("values").EnumerateObject()];
        foreach (JsonProperty seed in seeds)
        {
            var rand = new MsvcRandom(uint.Parse(seed.Name, CultureInfo.InvariantCulture));
            Assert.All(seed.Value.EnumerateArray().Select(v => v.GetInt32()), expected => Assert.Equal(expected, rand.Rand()));
        }

        Assert.Equal(5, seeds.Length);
        var one = new MsvcRandom(1);
        Assert.Equal([41, 18467, 6334, 26500], new[] { one.Rand(), one.Rand(), one.Rand(), one.Rand() });
    }

    [Theory]
    [MemberData(nameof(SequenceIndexes))]
    public void Sequence_matches_golden(int index)
    {
        JsonElement sequence = Golden("sequences", index);
        int[] expected = sequence.GetProperty("indices").EnumerateArray().Select(i => i.GetInt32()).ToArray();

        int?[] actual = Run(sequence.GetProperty("songCount").GetInt32(), sequence.GetProperty("seed").GetUInt32(), expected.Length, [], []);

        Assert.Equal(expected.Select(i => (int?)i), actual);
    }

    [Theory]
    [MemberData(nameof(ScenarioIndexes))]
    public void Scenario_matches_golden(int index)
    {
        JsonElement scenario = Golden("scenarios", index);
        int?[] expected = scenario.GetProperty("indices").EnumerateArray()
            .Select(i => i.ValueKind == JsonValueKind.Null ? (int?)null : i.GetInt32())
            .ToArray();
        int[] rescans = scenario.GetProperty("rescanBeforePick").EnumerateArray().Select(i => i.GetInt32()).ToArray();
        int[] disabled = scenario.GetProperty("disabled").EnumerateArray().Select(i => i.GetInt32()).ToArray();

        int?[] actual = Run(scenario.GetProperty("songCount").GetInt32(), scenario.GetProperty("seed").GetUInt32(), expected.Length, rescans, disabled);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_round_plays_every_song_once_and_never_repeats_across_rounds()
    {
        var bag = new ShuffleBag(new MsvcRandom(7));
        string[] ids = Enumerable.Range(0, 9).Select(i => $"bundled:{i}.mid").ToArray();
        bag.Rescan(ids);

        string[] picks = Enumerable.Range(0, 9 + 8 * 10).Select(_ => bag.Pick(_ => true)!).ToArray();

        // The first round holds every song; each later round holds every song except the one that ended the round before.
        Assert.Equal(ids.Order(), picks.Take(9).Order());
        for (int start = 9; start < picks.Length; start += 8)
        {
            Assert.Equal(ids.Where(id => id != picks[start - 1]).Order(), picks.Skip(start).Take(8).Order());
        }

        Assert.DoesNotContain(picks.Zip(picks.Skip(1)), pair => pair.First == pair.Second);
    }

    [Fact]
    public void No_eligible_song_draws_no_random_number()
    {
        var random = new ScriptedRandom();
        var bag = new ShuffleBag(random);
        bag.Rescan(["bundled:a.mid", "bundled:b.mid"]);

        Assert.Null(bag.Pick(_ => false));
        Assert.Empty(random.Requests);
    }

    /// <summary>Runs the bag like the generator of <c>shuffle.json</c>: indices are songs "0".."n-1"; a rescan just before pick k keeps the last pick.</summary>
    private static int?[] Run(int songCount, uint seed, int picks, int[] rescanBefore, int[] disabled)
    {
        string[] ids = Enumerable.Range(0, songCount).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        var bag = new ShuffleBag(new MsvcRandom(seed));
        bag.Rescan(ids);
        var excluded = disabled.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var result = new int?[picks];
        for (int k = 0; k < picks; k++)
        {
            if (rescanBefore.Contains(k))
            {
                bag.Rescan(ids);
            }

            string? pick = bag.Pick(id => !excluded.Contains(id));
            result[k] = pick is null ? null : int.Parse(pick, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static JsonElement Golden(string list, int index)
    {
        using JsonDocument golden = GoldenData.ReadJson("shuffle.json");
        return golden.RootElement.GetProperty(list)[index].Clone();
    }

    private static TheoryData<int> Indexes(string list)
    {
        using JsonDocument golden = GoldenData.ReadJson("shuffle.json");
        var data = new TheoryData<int>();
        for (int i = 0; i < golden.RootElement.GetProperty(list).GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }
}
