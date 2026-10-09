using System.Diagnostics;

namespace HolidayLights.Rendering.Composition;

/// <summary>
/// One cubic piece of an animation function: <c>v(t) = C0 + C1 t + C2 t^2 + C3 t^3</c>, with <c>t</c> in seconds measured from
/// <see cref="OffsetSeconds"/> (the DirectComposition <c>AddCubic</c> convention).
/// </summary>
internal readonly record struct CubicSegment(double OffsetSeconds, double C0, double C1, double C2, double C3)
{
    /// <summary>Evaluates the polynomial <paramref name="seconds"/> after the segment start.</summary>
    public double Evaluate(double seconds) => C0 + seconds * (C1 + seconds * (C2 + seconds * C3));

    /// <summary>Returns the segment with every coefficient multiplied by <paramref name="factor"/>.</summary>
    public CubicSegment Scale(double factor) => new(OffsetSeconds, C0 * factor, C1 * factor, C2 * factor, C3 * factor);

    /// <summary>
    /// The same polynomial entered <paramref name="into"/> seconds after its start, placed at <paramref name="offset"/>
    /// (Taylor re-expansion: <c>q(u) = p(u + into)</c>).
    /// </summary>
    public CubicSegment EnteredAt(double into, double offset) => new(
        offset,
        Evaluate(into),
        C1 + into * (2 * C2 + 3 * C3 * into),
        C2 + 3 * C3 * into,
        C3);
}

/// <summary>
/// An opacity over time anchored at an absolute <see cref="Stopwatch"/> timestamp: a list of cubic segments that either ends
/// at a final value or repeats. It is a pure description; <see cref="CompositionDevice.CreateAnimation"/> re-anchors it at the
/// moment it is committed (<see cref="StartingAt"/>) and turns it into a DirectComposition animation, and
/// <see cref="Evaluate"/> lets tests check the shape.
/// </summary>
internal sealed class OpacityCurve
{
    private readonly CubicSegment[] segments;

    private OpacityCurve(long beginTimestamp, CubicSegment[] segments, double terminalOffsetSeconds, float endValue, double repeatSeconds)
    {
        BeginTimestamp = beginTimestamp;
        this.segments = segments;
        TerminalOffsetSeconds = terminalOffsetSeconds;
        EndValue = endValue;
        RepeatSeconds = repeatSeconds;
    }

    /// <summary>The <see cref="Stopwatch"/> timestamp at which offset 0 of the function lies.</summary>
    public long BeginTimestamp { get; }

    /// <summary>The segments in offset order (empty for a constant).</summary>
    public IReadOnlyList<CubicSegment> Segments => segments;

    /// <summary>The offset of the end (non-repeating) or of the repeat point (repeating), in seconds.</summary>
    public double TerminalOffsetSeconds { get; }

    /// <summary>The value after the end (non-repeating curves), or the constant value.</summary>
    public float EndValue { get; }

    /// <summary>Length of the repeated part: from <see cref="TerminalOffsetSeconds"/> on, the preceding <c>RepeatSeconds</c> repeat forever (0 = the curve ends).</summary>
    public double RepeatSeconds { get; }

    /// <summary>True when the end of the curve repeats forever.</summary>
    public bool Repeats => RepeatSeconds > 0;

    /// <summary>True for a constant value (no animation needed).</summary>
    public bool IsConstant => segments.Length == 0;

    /// <summary>Creates a constant.</summary>
    public static OpacityCurve Constant(float value) => new(0, [], 0, Clamp(value), repeatSeconds: 0);

    /// <summary>Creates a curve from built segments (see <see cref="OpacityCurveBuilder"/>).</summary>
    internal static OpacityCurve Create(long beginTimestamp, CubicSegment[] segments, double terminalOffsetSeconds, float endValue, double repeatSeconds) =>
        segments.Length == 0 ? Constant(endValue) : new(beginTimestamp, segments, terminalOffsetSeconds, Clamp(endValue), repeatSeconds);

    /// <summary>Evaluates the curve at a timestamp (times before the begin take the first value).</summary>
    public double Evaluate(long timestamp)
    {
        if (IsConstant)
        {
            return EndValue;
        }

        double t = Math.Max(0, (timestamp - BeginTimestamp) / (double)Stopwatch.Frequency);
        if (Repeats)
        {
            if (t >= TerminalOffsetSeconds)
            {
                t = TerminalOffsetSeconds - RepeatSeconds + (t - TerminalOffsetSeconds) % RepeatSeconds;
            }
        }
        else if (t >= TerminalOffsetSeconds)
        {
            return EndValue;
        }

        int index = segments.Length - 1;
        while (index > 0 && segments[index].OffsetSeconds > t)
        {
            index--;
        }

        CubicSegment segment = segments[index];
        return Math.Clamp(segment.Evaluate(t - segment.OffsetSeconds), 0.0, 1.0);
    }

    /// <summary>
    /// The same function with its offset 0 at <paramref name="timestamp"/>: a fade that began at a step boundary a few
    /// milliseconds ago continues where it is, a wave anchored at the clock epoch continues in phase. A later timestamp drops
    /// what lies before it (a repeating period is rotated to start there); an earlier one holds the first value until the begin.
    /// </summary>
    public OpacityCurve StartingAt(long timestamp)
    {
        if (IsConstant || timestamp == BeginTimestamp)
        {
            return this;
        }

        double shift = (timestamp - BeginTimestamp) / (double)Stopwatch.Frequency;
        if (shift < 0)
        {
            var held = new CubicSegment[segments.Length + 1];
            held[0] = new CubicSegment(0, segments[0].C0, 0, 0, 0);
            for (int i = 0; i < segments.Length; i++)
            {
                held[i + 1] = segments[i] with { OffsetSeconds = segments[i].OffsetSeconds - shift };
            }

            return new OpacityCurve(timestamp, held, TerminalOffsetSeconds - shift, EndValue, RepeatSeconds);
        }

        double repeatStart = TerminalOffsetSeconds - RepeatSeconds;
        if (!Repeats && shift >= TerminalOffsetSeconds)
        {
            return Constant(EndValue);
        }

        if (!Repeats || shift < repeatStart)
        {
            return new OpacityCurve(timestamp, [.. Slice(shift, TerminalOffsetSeconds, 0)], TerminalOffsetSeconds - shift, EndValue, RepeatSeconds);
        }

        // Inside the repeating part: rotate one period so that it starts at the current phase, then repeat that.
        double phase = (shift - repeatStart) % RepeatSeconds;
        var rotated = new List<CubicSegment>(Slice(repeatStart + phase, TerminalOffsetSeconds, 0));
        rotated.AddRange(Slice(repeatStart, repeatStart + phase, RepeatSeconds - phase));
        return new OpacityCurve(timestamp, [.. rotated], RepeatSeconds, EndValue, RepeatSeconds);
    }

    /// <summary>Returns the curve multiplied by <paramref name="factor"/> (glow intensity).</summary>
    public OpacityCurve Scale(float factor)
    {
        if (factor == 1f)
        {
            return this;
        }

        if (IsConstant)
        {
            return Constant(EndValue * factor);
        }

        var scaled = new CubicSegment[segments.Length];
        for (int i = 0; i < scaled.Length; i++)
        {
            scaled[i] = segments[i].Scale(factor);
        }

        return new OpacityCurve(BeginTimestamp, scaled, TerminalOffsetSeconds, Clamp(EndValue * factor), RepeatSeconds);
    }

    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>The segments covering offsets [<paramref name="from"/>, <paramref name="to"/>), re-based to start at <paramref name="at"/>.</summary>
    private IEnumerable<CubicSegment> Slice(double from, double to, double at)
    {
        const double Epsilon = 1e-12;
        for (int i = 0; i < segments.Length; i++)
        {
            double start = segments[i].OffsetSeconds;
            double end = i + 1 < segments.Length ? segments[i + 1].OffsetSeconds : TerminalOffsetSeconds;
            if (end <= from + Epsilon || start >= to - Epsilon)
            {
                continue;
            }

            yield return start >= from
                ? segments[i] with { OffsetSeconds = at + start - from }
                : segments[i].EnteredAt(from - start, at);
        }
    }
}

/// <summary>Builds an <see cref="OpacityCurve"/> segment by segment from offset 0.</summary>
internal sealed class OpacityCurveBuilder
{
    private readonly long beginTimestamp;
    private readonly List<CubicSegment> segments = [];
    private double cursor;

    /// <summary>Starts a curve whose offset 0 is <paramref name="beginTimestamp"/>.</summary>
    public OpacityCurveBuilder(long beginTimestamp) => this.beginTimestamp = beginTimestamp;

    /// <summary>The offset (seconds) where the next segment starts.</summary>
    public double Cursor => cursor;

    /// <summary>Holds a value for a duration.</summary>
    public OpacityCurveBuilder Hold(double value, double seconds) => Add(seconds, value, 0, 0, 0);

    /// <summary>Changes linearly from one value to another.</summary>
    public OpacityCurveBuilder Linear(double from, double to, double seconds) =>
        seconds <= 0 ? this : Add(seconds, from, (to - from) / seconds, 0, 0);

    /// <summary>Cubic ease-out (<c>1 - (1 - u)^3</c>) from one value to another.</summary>
    public OpacityCurveBuilder EaseOut(double from, double to, double seconds)
    {
        if (seconds <= 0)
        {
            return this;
        }

        double delta = to - from;
        return Add(seconds, from, 3 * delta / seconds, -3 * delta / (seconds * seconds), delta / (seconds * seconds * seconds));
    }

    /// <summary>A cubic Hermite piece matching values and slopes (per second) at both ends.</summary>
    public OpacityCurveBuilder Hermite(double value0, double value1, double slope0, double slope1, double seconds)
    {
        if (seconds <= 0)
        {
            return this;
        }

        double h = seconds;
        double c2 = (3 * (value1 - value0) / h - 2 * slope0 - slope1) / h;
        double c3 = (2 * (value0 - value1) / h + slope0 + slope1) / (h * h);
        return Add(seconds, value0, slope0, c2, c3);
    }

    /// <summary>Finishes the curve: after the last segment the value stays <paramref name="endValue"/>.</summary>
    public OpacityCurve End(double endValue) => OpacityCurve.Create(beginTimestamp, [.. segments], cursor, (float)endValue, repeatSeconds: 0);

    /// <summary>Finishes the curve: everything built after <paramref name="fromOffsetSeconds"/> repeats forever.</summary>
    public OpacityCurve RepeatFrom(double fromOffsetSeconds) =>
        OpacityCurve.Create(beginTimestamp, [.. segments], cursor, 0f, repeatSeconds: cursor - fromOffsetSeconds);

    private OpacityCurveBuilder Add(double seconds, double c0, double c1, double c2, double c3)
    {
        if (seconds > 0)
        {
            segments.Add(new CubicSegment(cursor, c0, c1, c2, c3));
            cursor += seconds;
        }

        return this;
    }
}
