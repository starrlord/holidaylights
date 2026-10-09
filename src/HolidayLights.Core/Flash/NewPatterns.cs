namespace HolidayLights.Core.Flash;

/// <summary>
/// The step-driven new patterns of PRODUCT-SPEC 5.7 that need no state: Chase Around the Screen, and the frames and step
/// brightness of the continuous patterns (Slow Glow, Waves, idle Dance).
/// </summary>
internal static class NewPatterns
{
    /// <summary>The period of Slow Glow and Waves in steps (4.8 s per breath at the default speed).</summary>
    public const double WavePeriodSteps = 16;

    /// <summary>The Slow Glow brightness of a segment that started at <paramref name="segmentStart"/>: <c>0.5 - 0.5 cos(2 pi t / 16P)</c>.</summary>
    /// <param name="segmentStart">The step at which the pattern (or its Combination segment) started.</param>
    /// <returns>The wave.</returns>
    public static BrightnessWave SlowGlow(long segmentStart) => new(WavePeriodSteps, Mod(segmentStart, 16) / WavePeriodSteps);

    /// <summary>The Waves brightness of light bulb <c>q</c>: <c>0.5 - 0.5 cos(2 pi (t / 16P - q / 16))</c>, travelling clockwise.</summary>
    /// <param name="lightIndex">q.</param>
    /// <param name="segmentStart">The step at which the pattern (or its Combination segment) started.</param>
    /// <returns>The wave.</returns>
    public static BrightnessWave Waves(int lightIndex, long segmentStart) => new(WavePeriodSteps, Mod(segmentStart + lightIndex, 16) / WavePeriodSteps);

    /// <summary>
    /// The brightness of a continuous pattern held for a whole step when Smooth Fading is off: lit while the wave is at
    /// 0.5 or more (its crossings fall on step boundaries, so the middle of the step decides).
    /// </summary>
    /// <param name="wave">The wave.</param>
    /// <param name="step">The step.</param>
    /// <returns>1 or 0.</returns>
    public static float SteppedBrightness(BrightnessWave wave, long step) => wave.Evaluate(step + 0.5) >= 0.5 ? 1f : 0f;

    /// <summary>Evaluates a continuous pattern (Slow Glow or Waves) at a step: frames like Flash Together, light bulbs from the wave.</summary>
    /// <param name="waves">True for Waves, false for Slow Glow.</param>
    /// <param name="step">The step.</param>
    /// <param name="segmentStart">The step at which the pattern (or its Combination segment) started.</param>
    /// <param name="fading">Smooth Fading: the wave at the step start; otherwise <see cref="SteppedBrightness"/>.</param>
    /// <param name="bulbs">The bulb facts.</param>
    /// <param name="states">Receives one state per bulb.</param>
    public static void EvaluateContinuous(bool waves, long step, long segmentStart, bool fading, FlashBulbTable bulbs, Span<BulbVisualState> states)
    {
        // Slow Glow has one brightness for every bulb, Waves one per q mod 16.
        Span<float> brightness = stackalloc float[waves ? (int)WavePeriodSteps : 1];
        for (int k = 0; k < brightness.Length; k++)
        {
            BrightnessWave wave = waves ? Waves(k, segmentStart) : SlowGlow(segmentStart);
            brightness[k] = fading ? (float)wave.Evaluate(step) : SteppedBrightness(wave, step);
        }

        for (int o = 0; o < states.Length; o++)
        {
            switch (bulbs.Kinds[o])
            {
                case BulbAnimationKind.LightBulb:
                    states[o] = bulbs.ShowBrightness(o, brightness[waves ? bulbs.LightIndices[o] % brightness.Length : 0]);
                    break;
                case BulbAnimationKind.Animation:
                    states[o] = bulbs.ShowFrame(o, (int)Mod(step - segmentStart, bulbs.FrameCounts[o]));
                    break;
                default:
                    states[o] = bulbs.ShowFrame(o, 0);
                    break;
            }
        }
    }

    /// <summary>
    /// Chase Around the Screen (pattern 7): a light bulb is lit when <c>(q - s) mod 3 = 0</c>, one in three, moving one bulb
    /// clockwise per step; animation bulbs show frame <c>(s - a) mod N</c>, so their motion travels clockwise too. A lit bulb
    /// stays dark for two steps, so it retriggers at most every 540 ms under "Limit Flashing" (interval 3 or more).
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="bulbs">The bulb facts.</param>
    /// <param name="states">Receives one state per bulb.</param>
    public static void EvaluateChaseAround(long step, FlashBulbTable bulbs, Span<BulbVisualState> states)
    {
        for (int o = 0; o < states.Length; o++)
        {
            switch (bulbs.Kinds[o])
            {
                case BulbAnimationKind.LightBulb:
                    bool lit = Mod(bulbs.LightIndices[o] - step, 3) == 0;
                    states[o] = bulbs.ShowFrame(o, lit ? bulbs.LitFrames[o] : 1 - bulbs.LitFrames[o]);
                    break;
                case BulbAnimationKind.Animation:
                    states[o] = bulbs.ShowFrame(o, (int)Mod(step - bulbs.AnimationIndices[o], bulbs.FrameCounts[o]));
                    break;
                default:
                    states[o] = bulbs.ShowFrame(o, 0);
                    break;
            }
        }
    }

    /// <summary>The mathematical modulo (never negative).</summary>
    /// <param name="value">The value.</param>
    /// <param name="modulus">A positive modulus.</param>
    /// <returns>0 to <paramref name="modulus"/> - 1.</returns>
    public static long Mod(long value, long modulus)
    {
        long r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}

/// <summary>
/// "Combination" (pattern 10, PRODUCT-SPEC 5.7): Flash Together, Alternating, Bulb Chase, Random Flashing, Twinkle, Slow
/// Glow, Chase Around the Screen and Waves in that order, 40 steps each, each restarting from step 0; then again.
/// </summary>
internal static class CombinationSchedule
{
    /// <summary>Steps each pattern plays (12 s at the default speed).</summary>
    public const int StepsPerPattern = 40;

    /// <summary>The patterns in playing order.</summary>
    public static IReadOnlyList<FlashPatternId> Order { get; } =
    [
        FlashPatternId.FlashTogether, FlashPatternId.Alternating, FlashPatternId.BulbChase, FlashPatternId.RandomFlashing,
        FlashPatternId.Twinkle, FlashPatternId.SlowGlow, FlashPatternId.ChaseAround, FlashPatternId.Waves,
    ];

    /// <summary>The pattern playing at a step.</summary>
    /// <param name="step">The Combination step (0 or more).</param>
    /// <returns>The pattern and the step at which its segment started.</returns>
    public static (FlashPatternId Pattern, long SegmentStart) At(long step)
    {
        long segment = step / StepsPerPattern;
        return (Order[(int)(segment % Order.Count)], segment * StepsPerPattern);
    }
}
