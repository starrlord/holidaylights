using System.Text.Json;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>
/// One case of the layout goldens (<c>Golden/layout/*.json.gz</c>) as a
/// <see cref="LightsLayout"/> of one display, with the phase each placement shows in each strip frame.
/// </summary>
/// <param name="Layout">The layout (scale 1, one display).</param>
/// <param name="Phases">Per placement ordinal: the phase shown in strip frame f (index f, up to 8 frames).</param>
/// <param name="StripFrames">Per placement ordinal: its strip's frame count W.</param>
internal sealed record GoldenLayout(LightsLayout Layout, IReadOnlyList<int[]> Phases, IReadOnlyList<int> StripFrames)
{
    private static readonly Side[] SideOrder = [Side.Top, Side.Bottom, Side.Right, Side.Left];

    /// <summary>Loads a case.</summary>
    /// <param name="theme">The golden file name without extension, e.g. <c>default</c>.</param>
    /// <param name="area">The case rectangle, e.g. <c>0,0,1920,1032</c>.</param>
    /// <param name="pattern">The classic pattern 0-4.</param>
    /// <param name="displayId">The display id to give the placements.</param>
    /// <param name="offsetX">Moves the whole layout (another display's position).</param>
    public static GoldenLayout Load(string theme, RectI area, int pattern, string displayId = "display-1", int offsetX = 0)
    {
        using JsonDocument document = GoldenData.ReadJson($"layout/{theme}.json.gz");
        JsonElement root = document.RootElement;
        var slugs = root.GetProperty("bulbs").EnumerateObject().ToDictionary(p => int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture), p => p.Value.GetString()!);
        JsonElement found = root.GetProperty("cases").EnumerateArray().Single(c =>
            c.GetProperty("pattern").GetInt32() == pattern &&
            c.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual([area.Left, area.Top, area.Right, area.Bottom]));

        var placements = new List<BulbPlacement>();
        var strips = new List<StripLayout>();
        var phases = new List<int[]>();
        var stripFrames = new List<int>();
        foreach (JsonElement strip in found.GetProperty("strips").EnumerateArray())
        {
            int stripIndex = strips.Count;
            int frames = strip.GetProperty("frames").GetInt32();
            var stripPlacements = new List<BulbPlacement>();
            foreach (JsonElement row in strip.GetProperty("placements").EnumerateArray())
            {
                JsonElement[] cells = [.. row.EnumerateArray()];
                int ordinal = placements.Count;
                var placement = new BulbPlacement
                {
                    Ordinal = ordinal,
                    DisplayId = displayId,
                    StripIndex = stripIndex,
                    Slot = (CellSlot)cells[0].GetInt32(),
                    Index = cells[1].GetInt32(),
                    BulbId = BulbIds.BuiltIn(slugs[cells[2].GetInt32()]),
                    Flavor = cells[3].GetInt32(),
                    Bounds = RectI.FromXYWH(cells[4].GetInt32() + offsetX, cells[5].GetInt32(), cells[6].GetInt32(), cells[7].GetInt32()),
                    ChaseIndex = cells[8].ValueKind == JsonValueKind.Null ? null : cells[8].GetInt32(),
                    RingId = 0,
                    RingIndex = ordinal,
                };
                placements.Add(placement);
                stripPlacements.Add(placement);
                phases.Add([.. cells[9].EnumerateArray().Select(v => v.GetInt32())]);
                stripFrames.Add(frames);
            }

            int[] rect = [.. strip.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32())];
            strips.Add(new StripLayout
            {
                Index = stripIndex,
                DisplayId = displayId,
                Side = SideOrder[stripIndex],
                Rect = new RectI(rect[0] + offsetX, rect[1], rect[2] + offsetX, rect[3]),
                Thickness = strip.GetProperty("thickness").GetInt32(),
                Count = strip.GetProperty("count").GetInt32(),
                Gap = strip.GetProperty("gap").ValueKind == JsonValueKind.Null ? double.PositiveInfinity : strip.GetProperty("gap").GetDouble(),
                Placements = stripPlacements,
            });
        }

        var target = new LayoutTarget(displayId, area.Offset(offsetX, 0), 1.0);
        var layout = new LightsLayout
        {
            Mode = FrameMode.EachDisplay,
            Arrangement = SlotAssignment.Empty,
            Displays = [new DisplayLayout(target, strips)],
            Placements = placements,
            Rings = [new LightsRing(0, [.. placements.Select(p => p.Ordinal)])],
        };
        return new GoldenLayout(layout, phases, stripFrames);
    }

    /// <summary>
    /// The states at a global frame counter, as the 5.4 renderer shows them: each strip shows frame
    /// <c>counter mod W</c>; a light bulb is lit (brightness and glow 1) when its frame is the lit one.
    /// </summary>
    public BulbVisualState[] StatesAt(int counter, IBulbResolver bulbs)
    {
        var states = new BulbVisualState[Layout.Placements.Count];
        foreach (BulbPlacement placement in Layout.Placements)
        {
            int[] phases = Phases[placement.Ordinal];
            int phase = phases[counter % StripFrames[placement.Ordinal] % phases.Length];
            Assert.True(bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb));
            BulbAnimationInfo animation = bulb.GetAnimation(placement.Slot, placement.Flavor);
            int frame = phase % animation.FrameCount;
            bool lit = animation.Kind == BulbAnimationKind.LightBulb && frame == animation.LitFrame;
            states[placement.Ordinal] = animation.Kind == BulbAnimationKind.LightBulb
                ? new BulbVisualState(frame, lit ? 1f : 0f, lit ? 1f : 0f)
                : new BulbVisualState(frame, 1f, 0f);
        }

        return states;
    }

    /// <summary>Combines layouts of several displays into one (dense ordinals in input order).</summary>
    public static LightsLayout Combine(params LightsLayout[] layouts)
    {
        var placements = new List<BulbPlacement>();
        var displays = new List<DisplayLayout>();
        foreach (LightsLayout layout in layouts)
        {
            int first = placements.Count;
            placements.AddRange(layout.Placements.Select(p => p with { Ordinal = first + p.Ordinal }));
            displays.AddRange(layout.Displays);
        }

        return new LightsLayout
        {
            Mode = FrameMode.EachDisplay,
            Arrangement = SlotAssignment.Empty,
            Displays = displays,
            Placements = placements,
            Rings = [new LightsRing(0, [.. placements.Select(p => p.Ordinal)])],
        };
    }
}
