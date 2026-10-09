namespace HolidayLights.Core.Flash;

/// <summary>
/// "Twinkle" (PRODUCT-SPEC 5.7, pattern 5): light bulbs start lit with probability 0.8; at each step a lit bulb goes dark
/// when <c>u &lt; 0.12</c> and a dark bulb lights when <c>u &lt; 0.5</c> (about 80 % lit). Animation bulbs advance one frame
/// at a step when <c>u &lt; 0.5</c>. Each ring draws <c>u</c> from its own seed (<see cref="DisplaySeeds.PatternSeed"/>), so
/// identical displays in "Each Display" mode twinkle independently while ring 0 keeps the exact sequence of its seed.
/// </summary>
/// <remarks>
/// <para>Stepping forward costs one <c>u</c> per bulb. Any other step is evaluated directly, so a new sequencer (a rebuild,
/// a preview joining the desktop clock after hours) never replays the whole history:</para>
/// <para><b>Light bulbs</b>, exactly: <c>u &lt; 0.12</c> toggles the bulb whatever its state, <c>0.12 &lt;= u &lt; 0.5</c>
/// lights it whatever its state, and <c>u &gt;= 0.5</c> changes nothing; so the state at step <c>s</c> is "lit" at the
/// latest lighting step (or the start state), flipped once per toggle after it. Looking back finds that step after 2.6
/// steps on average.</para>
/// <para><b>Animation bulbs</b> count advances, which needs history; to bound it, each bulb's count restarts every 1,024
/// steps, at its own offset, from a frame drawn from the same <c>u</c>, so evaluating any step replays at most 1,023
/// steps. A restart looks like one more random frame change of that bulb.</para>
/// <para>Under "Limit Flashing" the interval is at least 3 (180 ms) and a dark bulb needs two steps to light again, so a
/// bulb retriggers at most every 360 ms.</para>
/// </remarks>
internal sealed class TwinkleModel
{
    /// <summary>Probability that a light bulb starts lit.</summary>
    public const double StartLitProbability = 0.8;

    /// <summary>A lit bulb goes dark when <c>u</c> is below this.</summary>
    public const double GoDarkProbability = 0.12;

    /// <summary>A dark bulb lights when <c>u</c> is below this.</summary>
    public const double LightUpProbability = 0.5;

    /// <summary>An animation bulb advances a frame when <c>u</c> is below this.</summary>
    public const double AdvanceProbability = 0.5;

    /// <summary>Steps between restarts of an animation bulb's frame count.</summary>
    public const int AnchorPeriod = 1024;

    private readonly FlashBulbTable bulbs;
    private readonly ulong[] keys;
    private readonly int[] anchorOffsets;
    private readonly bool[] lit;
    private readonly int[] frames;
    private long step = -1;

    /// <summary>Creates the model.</summary>
    /// <param name="bulbs">The bulb facts.</param>
    /// <param name="seed">The pattern seed (ring 0's seed; other rings use <see cref="DisplaySeeds.PatternSeed"/>).</param>
    public TwinkleModel(FlashBulbTable bulbs, ulong seed)
    {
        this.bulbs = bulbs;
        keys = new ulong[bulbs.Count];
        anchorOffsets = new int[bulbs.Count];
        lit = new bool[bulbs.Count];
        frames = new int[bulbs.Count];
        for (int o = 0; o < bulbs.Count; o++)
        {
            keys[o] = PatternRandom.Key(DisplaySeeds.PatternSeed(seed, bulbs.RingIds[o]), bulbs.RingIndices[o]);

            // Animation bulbs never use u(i, 0) otherwise; it spreads their restart steps over 1..AnchorPeriod.
            anchorOffsets[o] = 1 + (int)(PatternRandom.SplitMix64(keys[o]) % AnchorPeriod);
        }
    }

    /// <summary>Evaluates a step (one step forward is incremental; anything else is evaluated directly).</summary>
    /// <param name="target">The step (0 or more).</param>
    /// <param name="states">Receives one state per bulb.</param>
    public void Evaluate(long target, Span<BulbVisualState> states)
    {
        bool forward = target == step + 1 && step >= 0;
        for (int o = 0; o < states.Length; o++)
        {
            switch (bulbs.Kinds[o])
            {
                case BulbAnimationKind.LightBulb:
                    lit[o] = forward ? Transition(lit[o], PatternRandom.Uniform(keys[o], target)) : LitAt(keys[o], target);
                    states[o] = bulbs.ShowFrame(o, lit[o] ? bulbs.LitFrames[o] : 1 - bulbs.LitFrames[o]);
                    break;
                case BulbAnimationKind.Animation:
                    frames[o] = forward ? Advance(o, frames[o], target) : FrameAt(o, target);
                    states[o] = bulbs.ShowFrame(o, frames[o]);
                    break;
                default:
                    states[o] = bulbs.ShowFrame(o, 0);
                    break;
            }
        }

        step = target;
    }

    private static bool Transition(bool isLit, double u) => isLit ? u >= GoDarkProbability : u < LightUpProbability;

    private static bool LitAt(ulong key, long target)
    {
        bool toggled = false;
        for (long j = target; j >= 1; j--)
        {
            double u = PatternRandom.Uniform(key, j);
            if (u < GoDarkProbability)
            {
                toggled = !toggled;
            }
            else if (u < LightUpProbability)
            {
                return !toggled;
            }
        }

        bool startLit = PatternRandom.Uniform(key, 0) < StartLitProbability;
        return startLit != toggled;
    }

    private int Advance(int o, int frame, long target)
    {
        double u = PatternRandom.Uniform(keys[o], target);
        if (IsAnchor(o, target))
        {
            return (int)(u * bulbs.FrameCounts[o]);
        }

        return u < AdvanceProbability ? (frame + 1) % bulbs.FrameCounts[o] : frame;
    }

    private int FrameAt(int o, long target)
    {
        long anchor = 0;
        int frame = 0;
        int offset = anchorOffsets[o];
        if (target >= offset)
        {
            anchor = offset + ((target - offset) / AnchorPeriod * AnchorPeriod);
            frame = (int)(PatternRandom.Uniform(keys[o], anchor) * bulbs.FrameCounts[o]);
        }

        for (long j = anchor + 1; j <= target; j++)
        {
            if (PatternRandom.Uniform(keys[o], j) < AdvanceProbability)
            {
                frame = (frame + 1) % bulbs.FrameCounts[o];
            }
        }

        return frame;
    }

    private bool IsAnchor(int o, long target) => target >= anchorOffsets[o] && (target - anchorOffsets[o]) % AnchorPeriod == 0;
}
