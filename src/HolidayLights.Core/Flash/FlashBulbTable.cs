namespace HolidayLights.Core.Flash;

/// <summary>
/// Per-bulb facts of a layout, in flat arrays indexed by placement ordinal (the inner loops read them every step): the
/// animation kind and frames, the 5.4 strip facts (phase count of the strip, chase counter) and the ring facts of the new
/// patterns (ring id, ring index <c>i</c>, light-bulb index <c>q</c>, animation index <c>a</c>, Dance group).
/// </summary>
internal sealed class FlashBulbTable
{
    private FlashBulbTable(int count)
    {
        Kinds = new BulbAnimationKind[count];
        FrameCounts = new int[count];
        LitFrames = new int[count];
        StripPhaseCounts = new int[count];
        ChaseIndices = new int[count];
        IsCorner = new bool[count];
        RingIds = new int[count];
        RingIndices = new int[count];
        LightIndices = new int[count];
        AnimationIndices = new int[count];
        MusicGroups = new int[count];
    }

    /// <summary>Number of bulbs.</summary>
    public int Count => Kinds.Length;

    /// <summary>Static, light bulb or animation.</summary>
    public BulbAnimationKind[] Kinds { get; }

    /// <summary>Frames of each bulb's animation (1 or more).</summary>
    public int[] FrameCounts { get; }

    /// <summary>The lit frame of light bulbs (0 or 1); 0 otherwise.</summary>
    public int[] LitFrames { get; }

    /// <summary>
    /// The largest 5.4 phase count of the bulb's strip: every usable id of its edge (not only the placed ones) and, on top
    /// and bottom strips, the corners it draws (their phase count for that side), as the 5.4 edge-id compaction and the
    /// horizontal layouts compute it.
    /// </summary>
    public int[] StripPhaseCounts { get; }

    /// <summary>The 5.4 chase counter <c>k</c> of side bulbs; 0 for corners.</summary>
    public int[] ChaseIndices { get; }

    /// <summary>True for corner bulbs (they ignore the classic patterns).</summary>
    public bool[] IsCorner { get; }

    /// <summary>The ring of each bulb (<see cref="BulbPlacement.RingId"/>): one per display in "Each Display", one per wreath in "All Displays Together".</summary>
    public int[] RingIds { get; }

    /// <summary>The position <c>i</c> of each bulb in its ring.</summary>
    public int[] RingIndices { get; }

    /// <summary>q: the index among light bulbs in ring order, or -1.</summary>
    public int[] LightIndices { get; }

    /// <summary>a: the index among animation bulbs in ring order, or -1.</summary>
    public int[] AnimationIndices { get; }

    /// <summary>The Dance group: <c>q mod 12</c> for side light bulbs, <see cref="DanceEnvelope.CornerGroup"/> for corner light bulbs, -1 otherwise.</summary>
    public int[] MusicGroups { get; }

    /// <summary>Derives the facts of every placement.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="bulbs">Resolves its bulbs; a bulb that no longer resolves is treated as static.</param>
    /// <returns>The table.</returns>
    public static FlashBulbTable Build(LightsLayout layout, IBulbResolver bulbs)
    {
        var table = new FlashBulbTable(layout.Placements.Count);
        foreach (BulbPlacement placement in layout.Placements)
        {
            table.Describe(placement, bulbs);
        }

        table.MeasureStrips(layout, bulbs);
        table.WalkRings(layout);
        return table;
    }

    /// <summary>The 5.4 frame count W of a bulb's strip for a pattern: 1 for Don't Flash, 8 for Random Flashing, else the strip's largest phase count.</summary>
    /// <param name="ordinal">The bulb.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>W (1 or more).</returns>
    public int StripFrameCount(int ordinal, FlashPatternId pattern) => pattern switch
    {
        FlashPatternId.DontFlash => 1,
        FlashPatternId.RandomFlashing => RandomFlashingTable.FramesPerStrip,
        _ => Math.Max(1, StripPhaseCounts[ordinal]),
    };

    /// <summary>The steady state of "Stop Flashing" (PRODUCT-SPEC 5.12.3): light bulbs lit, everything else on frame 0.</summary>
    /// <param name="ordinal">The bulb.</param>
    /// <returns>The state.</returns>
    public BulbVisualState Steady(int ordinal) =>
        Kinds[ordinal] == BulbAnimationKind.LightBulb ? new BulbVisualState(LitFrames[ordinal], 1f, 1f) : new BulbVisualState(0, 1f, 0f);

    /// <summary>The state of a bulb showing a frame: light bulbs are lit on their lit frame and dark on the other one.</summary>
    /// <param name="ordinal">The bulb.</param>
    /// <param name="frame">The frame (0 to frame count - 1).</param>
    /// <returns>The state.</returns>
    public BulbVisualState ShowFrame(int ordinal, int frame)
    {
        if (Kinds[ordinal] != BulbAnimationKind.LightBulb)
        {
            return new BulbVisualState(frame, 1f, 0f);
        }

        return frame == LitFrames[ordinal] ? new BulbVisualState(frame, 1f, 1f) : new BulbVisualState(frame, 0f, 0f);
    }

    /// <summary>The state of a light bulb at a brightness (frame: lit from 0.5 up).</summary>
    /// <param name="ordinal">A light bulb.</param>
    /// <param name="brightness">0-1.</param>
    /// <returns>The state.</returns>
    public BulbVisualState ShowBrightness(int ordinal, float brightness)
    {
        int lit = LitFrames[ordinal];
        return new BulbVisualState(brightness >= 0.5f ? lit : 1 - lit, brightness, brightness);
    }

    private void Describe(BulbPlacement placement, IBulbResolver bulbs)
    {
        int o = placement.Ordinal;
        IsCorner[o] = placement.IsCorner;
        ChaseIndices[o] = placement.ChaseIndex ?? 0;
        RingIds[o] = placement.RingId;
        RingIndices[o] = placement.RingIndex;
        FrameCounts[o] = 1;
        Kinds[o] = BulbAnimationKind.Static;
        if (!bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb))
        {
            return;
        }

        BulbAnimationInfo animation = bulb.GetAnimation(placement.Slot, placement.Flavor);
        int frames = Math.Max(1, animation.FrameCount);
        FrameCounts[o] = frames;
        Kinds[o] = frames == 1 ? BulbAnimationKind.Static
            : animation.Kind == BulbAnimationKind.LightBulb && frames == 2 ? BulbAnimationKind.LightBulb
            : BulbAnimationKind.Animation;
        LitFrames[o] = Kinds[o] == BulbAnimationKind.LightBulb ? Math.Clamp(animation.LitFrame, 0, 1) : 0;
    }

    private void MeasureStrips(LightsLayout layout, IBulbResolver bulbs)
    {
        var edgePhases = new Dictionary<Side, int>();
        foreach (DisplayLayout display in layout.Displays)
        {
            foreach (StripLayout strip in display.Strips)
            {
                if (!edgePhases.TryGetValue(strip.Side, out int phases))
                {
                    phases = layout.Arrangement.GetEdge(strip.Side)
                        .Select(id => bulbs.TryGetBulb(id, out IBulb? bulb) ? bulb.GetPhaseCount(strip.Side) : 0)
                        .DefaultIfEmpty(0)
                        .Max();
                    edgePhases.Add(strip.Side, phases);
                }

                foreach (BulbPlacement corner in strip.Placements.Where(p => p.IsCorner))
                {
                    if (bulbs.TryGetBulb(corner.BulbId, out IBulb? bulb))
                    {
                        phases = Math.Max(phases, bulb.GetPhaseCount(strip.Side));
                    }
                }

                foreach (BulbPlacement placement in strip.Placements)
                {
                    StripPhaseCounts[placement.Ordinal] = phases;
                }
            }
        }
    }

    private void WalkRings(LightsLayout layout)
    {
        Array.Fill(LightIndices, -1);
        Array.Fill(AnimationIndices, -1);
        Array.Fill(MusicGroups, -1);
        foreach (LightsRing ring in layout.Rings)
        {
            int lights = 0;
            int animations = 0;
            foreach (int o in ring.Ordinals)
            {
                if (Kinds[o] == BulbAnimationKind.LightBulb)
                {
                    LightIndices[o] = lights;
                    MusicGroups[o] = IsCorner[o] ? DanceEnvelope.CornerGroup : lights % 12;
                    lights++;
                }
                else if (Kinds[o] == BulbAnimationKind.Animation)
                {
                    AnimationIndices[o] = animations++;
                }
            }
        }
    }
}
