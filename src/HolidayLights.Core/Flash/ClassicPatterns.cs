namespace HolidayLights.Core.Flash;

/// <summary>
/// The five 5.4 flash patterns, exact (PRODUCT-SPEC 5.6), including the quirks the product
/// owner keeps: Alternating stops alternating next to 4- and 8-phase bulbs, Bulb Chase runs toward the strip start,
/// corners ignore the pattern, Random Flashing repeats every 8 steps.
/// </summary>
/// <remarks>
/// <code>
/// W = frames of the strip (Don't Flash 1, Random 8, else its largest phase count); f = step mod W
/// side bulb phase:  Flash Together f; Alternating (k odd ? f + W/2 : f); Bulb Chase f + k; Random rand()
/// corner phase:     f
/// shown frame = phase mod (frame count of the animation)
/// </code>
/// "Don't Flash" shows frame 0 of every bulb, exactly as 5.4 (phase 0): built-in light bulbs are lit on it, add-on light
/// bulbs whose brighter frame is the second one show their darker first frame, as they did in 5.4. Only "Stop Flashing"
/// (<see cref="FlashBulbTable.Steady"/>) keeps every light bulb lit.
/// </remarks>
internal static class ClassicPatterns
{
    /// <summary>Evaluates a classic pattern at a step.</summary>
    /// <param name="pattern">Don't Flash, Flash Together, Alternating, Bulb Chase or Random Flashing.</param>
    /// <param name="step">The step (0 or more).</param>
    /// <param name="bulbs">The bulb facts.</param>
    /// <param name="random">The pre-rolled Random Flashing frames (required for Random Flashing).</param>
    /// <param name="states">Receives one state per bulb.</param>
    public static void Evaluate(FlashPatternId pattern, long step, FlashBulbTable bulbs, RandomFlashingTable? random, Span<BulbVisualState> states)
    {
        int[] frameCounts = bulbs.FrameCounts;
        int[] chase = bulbs.ChaseIndices;
        bool[] corners = bulbs.IsCorner;
        for (int o = 0; o < states.Length; o++)
        {
            if (pattern == FlashPatternId.DontFlash)
            {
                // W = 1, so every side and corner bulb shows phase 0 (PRODUCT-SPEC 5.6).
                states[o] = bulbs.ShowFrame(o, 0);
                continue;
            }

            int frames = frameCounts[o];
            if (frames == 1)
            {
                states[o] = bulbs.ShowFrame(o, 0);
                continue;
            }

            int w = bulbs.StripFrameCount(o, pattern);
            int f = FlashClock.StripFrame(step, w);
            int frame;
            if (corners[o])
            {
                frame = f % frames;
            }
            else
            {
                frame = pattern switch
                {
                    FlashPatternId.Alternating => ((chase[o] & 1) != 0 ? f + (w / 2) : f) % frames,
                    FlashPatternId.BulbChase => (f + chase[o]) % frames,
                    FlashPatternId.RandomFlashing => random!.Frame(o, f),
                    _ => f % frames,
                };
            }

            states[o] = bulbs.ShowFrame(o, frame);
        }
    }
}

/// <summary>
/// The 8 pre-rolled frames per side bulb of Random Flashing: one MSVC <c>rand()</c> per (bulb, frame) in build order (strips
/// Top, Bottom, Right, Left, bulb by bulb, frames 0-7; corners draw nothing), seeded once (5.4 used the millisecond).
/// </summary>
internal sealed class RandomFlashingTable
{
    /// <summary>W of Random Flashing: the pre-rolled frames loop every 8 steps.</summary>
    public const int FramesPerStrip = 8;

    private readonly int[] frames;

    /// <summary>Rolls the table.</summary>
    /// <param name="bulbs">The bulb facts; placement ordinals follow the build order.</param>
    /// <param name="seed">The <c>srand</c> seed.</param>
    public RandomFlashingTable(FlashBulbTable bulbs, uint seed)
    {
        frames = new int[bulbs.Count * FramesPerStrip];
        var random = new MsvcRandom(seed);
        for (int o = 0; o < bulbs.Count; o++)
        {
            if (bulbs.IsCorner[o])
            {
                continue;
            }

            for (int f = 0; f < FramesPerStrip; f++)
            {
                frames[(o * FramesPerStrip) + f] = random.Next() % bulbs.FrameCounts[o];
            }
        }
    }

    /// <summary>The frame a side bulb shows in strip frame <paramref name="stripFrame"/>.</summary>
    /// <param name="ordinal">The bulb.</param>
    /// <param name="stripFrame">0-7.</param>
    /// <returns>The frame.</returns>
    public int Frame(int ordinal, int stripFrame) => frames[(ordinal * FramesPerStrip) + stripFrame];
}
