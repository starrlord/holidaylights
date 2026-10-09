using System.Text.Json;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Layout;

/// <summary>Reads the golden layouts (<c>Golden/layout/*.json.gz</c>, described in <c>Golden/README.md</c>).</summary>
internal static class GoldenLayouts
{
    /// <summary>The golden files, for <c>[MemberData]</c>.</summary>
    public static TheoryData<string> Files
    {
        get
        {
            var files = new TheoryData<string>();
            foreach (string path in Directory.EnumerateFiles(TestPaths.Golden("layout"), "*.json.gz").Order(StringComparer.Ordinal))
            {
                files.Add(Path.GetFileName(path));
            }

            return files;
        }
    }

    /// <summary>Reads one golden file.</summary>
    /// <param name="fileName">E.g. <c>default.json.gz</c>.</param>
    /// <returns>The arrangement and every case.</returns>
    public static GoldenTheme Read(string fileName)
    {
        using JsonDocument document = GoldenData.ReadJson("layout/" + fileName);
        JsonElement root = document.RootElement;
        var slugs = root.GetProperty("bulbs").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetString()!);
        string Id(int legacyId) => BulbIds.BuiltIn(slugs[legacyId]);

        JsonElement settings = root.GetProperty("settings");
        int[] Row(string side) => [.. settings.GetProperty(side).EnumerateArray().Select(v => v.GetInt32())];
        string[] Edge(int[] row) => [.. row.Take(6).Where(v => v >= 0).Select(Id)];
        string? Corner(int value) => value >= 0 ? Id(value) : null;
        int[] top = Row("top");
        int[] right = Row("right");
        int[] bottom = Row("bottom");
        int[] left = Row("left");
        var arrangement = new SlotAssignment
        {
            Top = Edge(top),
            Right = Edge(right),
            Bottom = Edge(bottom),
            Left = Edge(left),
            TopLeft = Corner(top[6]),
            TopRight = Corner(top[7]),
            BottomLeft = Corner(bottom[6]),
            BottomRight = Corner(bottom[7]),
        };

        var cases = new List<GoldenCase>();
        foreach (JsonElement c in root.GetProperty("cases").EnumerateArray())
        {
            int[] rect = [.. c.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32())];
            JsonElement seed = c.GetProperty("seed");
            cases.Add(new GoldenCase(
                new RectI(rect[0], rect[1], rect[2], rect[3]),
                (FlashPatternId)c.GetProperty("pattern").GetInt32(),
                seed.ValueKind == JsonValueKind.Null ? null : seed.GetUInt32(),
                [.. c.GetProperty("strips").EnumerateArray().Select(s => ReadStrip(s, Id))]));
        }

        return new GoldenTheme(fileName, arrangement, cases);
    }

    private static GoldenStrip ReadStrip(JsonElement strip, Func<int, string> id)
    {
        int[] rect = [.. strip.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32())];
        JsonElement gap = strip.GetProperty("gap");
        Side side = strip.GetProperty("side").GetString() switch
        {
            "top" => Side.Top,
            "right" => Side.Right,
            "bottom" => Side.Bottom,
            _ => Side.Left,
        };
        var placements = strip.GetProperty("placements").EnumerateArray().Select(row =>
        {
            JsonElement[] v = [.. row.EnumerateArray()];
            return new GoldenPlacement(
                (CellSlot)v[0].GetInt32(),
                v[1].GetInt32(),
                id(v[2].GetInt32()),
                v[3].GetInt32(),
                RectI.FromXYWH(v[4].GetInt32(), v[5].GetInt32(), v[6].GetInt32(), v[7].GetInt32()),
                v[8].ValueKind == JsonValueKind.Null ? null : v[8].GetInt32(),
                [.. v[9].EnumerateArray().Select(p => p.GetInt32())]);
        }).ToArray();
        return new GoldenStrip(
            side,
            new RectI(rect[0], rect[1], rect[2], rect[3]),
            strip.GetProperty("thickness").GetInt32(),
            strip.GetProperty("frames").GetInt32(),
            strip.GetProperty("count").GetInt32(),
            gap.ValueKind == JsonValueKind.Null ? null : gap.GetDouble(),
            placements);
    }
}

/// <summary>One golden theme file.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="Arrangement">Its "Bulb Settings" as string ids.</param>
/// <param name="Cases">The 25 cases (5 rects x patterns 0-4).</param>
internal sealed record GoldenTheme(string FileName, SlotAssignment Arrangement, IReadOnlyList<GoldenCase> Cases);

/// <summary>One golden layout case.</summary>
/// <param name="Rect">The drawing rectangle.</param>
/// <param name="Pattern">The flash pattern.</param>
/// <param name="Seed">The <c>srand</c> seed (Random Flashing only).</param>
/// <param name="Strips">Top, bottom, right, left.</param>
internal sealed record GoldenCase(RectI Rect, FlashPatternId Pattern, uint? Seed, IReadOnlyList<GoldenStrip> Strips);

/// <summary>One golden strip.</summary>
/// <param name="Side">The side.</param>
/// <param name="Rect">The strip rectangle after layout.</param>
/// <param name="Thickness">The thickness.</param>
/// <param name="Frames">The world frame count W.</param>
/// <param name="Count">Side bulbs.</param>
/// <param name="Gap">The gap; null for +inf.</param>
/// <param name="Placements">Corners first, then side bulbs.</param>
internal sealed record GoldenStrip(Side Side, RectI Rect, int Thickness, int Frames, int Count, double? Gap, IReadOnlyList<GoldenPlacement> Placements);

/// <summary>One golden placement row.</summary>
/// <param name="Slot">The slot.</param>
/// <param name="Index">Side bulbs: i; corners: 0 or 1.</param>
/// <param name="BulbId">The bulb.</param>
/// <param name="Flavor">The raw flavor.</param>
/// <param name="Bounds">The cell rectangle.</param>
/// <param name="ChaseIndex">The chase counter; null for corners.</param>
/// <param name="Phases">The shown phase in world frames 0..min(W, 8) - 1.</param>
internal sealed record GoldenPlacement(CellSlot Slot, int Index, string BulbId, int Flavor, RectI Bounds, int? ChaseIndex, IReadOnlyList<int> Phases);
