using System.Diagnostics;
using HolidayLights.Rendering.Composition;

namespace HolidayLights.Rendering.Animation;

/// <summary>
/// The classic patterns as repeating animations. A strip shows its frame <c>s mod frames(strip)</c> (PRODUCT-SPEC 5.5.1, 5.6),
/// so the brightness of a light bulb repeats every <see cref="FlashBulbInfo.StripFrameCount"/> steps: it gets one animation of
/// a whole period, with the pattern's fades at the step boundaries, repeated forever, and the Lights thread has nothing to do
/// per step. Bulbs with the same sequence form a class and share their animations. A light bulb whose samples do not repeat
/// (or whose glow does not follow its brightness) is left to step-by-step updates.
/// </summary>
internal sealed class PeriodicBrightness
{
    private readonly int[] classOf;
    private readonly List<float[]> sequences;

    private PeriodicBrightness(int[] classOf, List<float[]> sequences)
    {
        this.classOf = classOf;
        this.sequences = sequences;
        Count = classOf.Count(c => c >= 0);
    }

    /// <summary>Number of light bulbs that follow a period.</summary>
    public int Count { get; }

    /// <summary>
    /// Samples two strip periods of every light bulb of a classic pattern that flashes (the classic patterns are stateless, so
    /// moving the sequencer is free) and groups the bulbs whose sequences repeat; the sequencer is moved back afterwards.
    /// </summary>
    /// <param name="sequencer">The sequencer, at the step it should stay at.</param>
    /// <param name="facts">Its facts per placement ordinal.</param>
    /// <returns>The classes (none for other patterns).</returns>
    public static PeriodicBrightness Learn(IFlashSequencer sequencer, IReadOnlyList<FlashBulbInfo> facts)
    {
        var classOf = new int[facts.Count];
        Array.Fill(classOf, -1);
        var sequences = new List<float[]>();
        FlashOptions options = sequencer.Options;
        bool flashes = FlashPatterns.IsClassic(options.Pattern) && options.Pattern != FlashPatternId.DontFlash && !options.StopFlashing;
        if (!flashes || !facts.Any(IsLightBulb))
        {
            return new PeriodicBrightness(classOf, sequences);
        }

        int longest = facts.Where(IsLightBulb).Max(Period);
        long resume = Math.Max(0, sequencer.Step);
        long now = Stopwatch.GetTimestamp();
        var samples = new BulbVisualState[2 * longest][];
        for (int step = 0; step < samples.Length; step++)
        {
            sequencer.MoveTo(step, now);
            samples[step] = sequencer.Current.ToArray();
        }

        sequencer.MoveTo(resume, now);
        var classes = new Dictionary<float[], int>(SequenceComparer.Instance);
        for (int ordinal = 0; ordinal < facts.Count; ordinal++)
        {
            int period = Period(facts[ordinal]);
            if (!IsLightBulb(facts[ordinal]) || !Repeats(samples, ordinal, period))
            {
                continue;
            }

            float[] sequence = new float[period];
            for (int k = 0; k < period; k++)
            {
                sequence[k] = samples[k][ordinal].Brightness;
            }

            if (!classes.TryGetValue(sequence, out int classId))
            {
                classId = sequences.Count;
                sequences.Add(sequence);
                classes[sequence] = classId;
            }

            classOf[ordinal] = classId;
        }

        return new PeriodicBrightness(classOf, sequences);
    }

    /// <summary>True when a bulb's brightness follows a learned period.</summary>
    public bool IsPeriodic(int ordinal) => classOf[ordinal] >= 0;

    /// <summary>The class of a periodic bulb (-1 for other bulbs).</summary>
    public int ClassOf(int ordinal) => classOf[ordinal];

    /// <summary>The brightness of a class at a step.</summary>
    public double ValueAt(int classId, long step)
    {
        float[] sequence = sequences[classId];
        return sequence[(int)(((step % sequence.Length) + sequence.Length) % sequence.Length)];
    }

    /// <summary>
    /// The animation of a class from <paramref name="begin"/>, a moment within <paramref name="step"/>: a fade from
    /// <paramref name="from"/> to the step's brightness, then from the next step boundary on one period of the pattern (a fade
    /// at each boundary where the brightness changes), repeated forever.
    /// </summary>
    /// <param name="classId">The class.</param>
    /// <param name="timing">The clock segment.</param>
    /// <param name="step">The step showing at <paramref name="begin"/>.</param>
    /// <param name="begin">Where the animation begins.</param>
    /// <param name="from">The brightness shown at <paramref name="begin"/>.</param>
    /// <param name="profile">The pattern's fades at this step period.</param>
    /// <returns>The lit visual's opacity (the glow is this times the glow intensity).</returns>
    public OpacityCurve Curve(int classId, WaveTiming timing, long step, long begin, double from, FadeProfile profile)
    {
        float[] sequence = sequences[classId];
        double value = ValueAt(classId, step);
        if (Array.TrueForAll(sequence, v => v == sequence[0]))
        {
            return OpacityCurves.Transition(begin, from, value, profile);
        }

        double stepSeconds = timing.StepPeriodMilliseconds / 1000;
        double firstSeconds = Math.Max(0, (timing.TimestampOfStep(step + 1) - begin) / (double)Stopwatch.Frequency);
        var builder = new OpacityCurveBuilder(begin);
        AppendStep(builder, from, value, firstSeconds, profile);
        double repeatFrom = builder.Cursor;
        for (long s = step + 1; s <= step + sequence.Length; s++)
        {
            AppendStep(builder, ValueAt(classId, s - 1), ValueAt(classId, s), stepSeconds, profile);
        }

        return builder.RepeatFrom(repeatFrom);
    }

    private static bool IsLightBulb(FlashBulbInfo facts) => facts.Kind == BulbAnimationKind.LightBulb;

    private static int Period(FlashBulbInfo facts) => Math.Max(1, facts.StripFrameCount);

    /// <summary>One step: the fade of PRODUCT-SPEC 5.8 (<c>TurnOn x delta</c> up, <c>TurnOff x delta</c> down), then the value held.</summary>
    private static void AppendStep(OpacityCurveBuilder builder, double from, double to, double seconds, FadeProfile profile)
    {
        double fadeMilliseconds = to >= from ? profile.TurnOnMilliseconds * (to - from) : profile.TurnOffMilliseconds * (from - to);
        double fade = Math.Min(fadeMilliseconds / 1000, seconds);
        builder.Linear(from, to, fade).Hold(to, seconds - fade);
    }

    /// <summary>True when a bulb's samples repeat with the period and its glow equals its brightness throughout.</summary>
    private static bool Repeats(BulbVisualState[][] samples, int ordinal, int period)
    {
        for (int k = 0; k < samples.Length; k++)
        {
            BulbVisualState state = samples[k][ordinal];
            if (state.Glow != state.Brightness || (k + period < samples.Length && samples[k + period][ordinal].Brightness != state.Brightness))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares brightness sequences by value.</summary>
    private sealed class SequenceComparer : IEqualityComparer<float[]>
    {
        public static SequenceComparer Instance { get; } = new();

        public bool Equals(float[]? x, float[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(float[] obj)
        {
            var hash = new HashCode();
            foreach (float value in obj)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
