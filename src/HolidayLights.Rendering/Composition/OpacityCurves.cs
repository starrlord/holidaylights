using System.Diagnostics;

namespace HolidayLights.Rendering.Composition;

/// <summary>The step clock segment a continuous wave is anchored to (one speed; see <see cref="StepClock"/>).</summary>
/// <param name="EpochTimestamp">Timestamp at which <paramref name="EpochStep"/> begins.</param>
/// <param name="EpochStep">The step that begins at the epoch.</param>
/// <param name="StepPeriodMilliseconds">P.</param>
internal readonly record struct WaveTiming(long EpochTimestamp, long EpochStep, double StepPeriodMilliseconds)
{
    private double PeriodTicks => StepPeriodMilliseconds * Stopwatch.Frequency / 1000.0;

    /// <summary>The segment of <paramref name="clock"/> in effect at <paramref name="timestamp"/> (the previous speed until a pending change starts).</summary>
    public static WaveTiming At(StepClock clock, long timestamp) =>
        timestamp < clock.EpochTimestamp && clock.Previous is { } previous
            ? new WaveTiming(previous.EpochTimestamp, previous.EpochStep, previous.StepPeriodMilliseconds)
            : new WaveTiming(clock.EpochTimestamp, clock.EpochStep, clock.StepPeriodMilliseconds);

    /// <summary>The step showing at a timestamp, as <see cref="StepClock.StepAt"/> computes it within this segment.</summary>
    public long StepAt(long timestamp) => (long)Math.Floor(EpochStep + (timestamp - EpochTimestamp) / PeriodTicks);

    /// <summary>When a step begins, as <see cref="StepClock.TimestampOfStep"/> computes it within this segment.</summary>
    public long TimestampOfStep(long step) => EpochTimestamp + (long)Math.Round((step - EpochStep) * PeriodTicks);
}

/// <summary>
/// The opacity shapes of PRODUCT-SPEC 5.8-5.10 and 4.3.2 as <see cref="OpacityCurve"/>s: fades, power-up rises, the Slow Glow
/// raised cosine (four cubic segments per period), its step version without Smooth Fading, and Dance envelopes.
/// </summary>
internal static class OpacityCurves
{
    /// <summary>Length of one exponential-decay piece of a Dance envelope (half the time constant).</summary>
    private const double DecayPieceMilliseconds = DanceEnvelope.DecayTauMilliseconds / 2;

    /// <summary>How long a Dance envelope decays before it is cut to 0 (six time constants: 0.25 % of the peak).</summary>
    private const double DecayLengthMilliseconds = DanceEnvelope.DecayTauMilliseconds * 6;

    /// <summary>A linear change from <paramref name="from"/> to <paramref name="to"/> starting at <paramref name="begin"/>.</summary>
    public static OpacityCurve Ramp(long begin, double from, double to, double durationMilliseconds) =>
        durationMilliseconds <= 0 || from == to
            ? OpacityCurve.Constant((float)to)
            : new OpacityCurveBuilder(begin).Linear(from, to, durationMilliseconds / 1000).End(to);

    /// <summary>
    /// A discrete brightness change with the fade profile of the pattern (PRODUCT-SPEC 5.8): increases take
    /// <c>TurnOn x |delta|</c>, decreases <c>TurnOff x |delta|</c> (0.6 d and d for a full switch).
    /// </summary>
    public static OpacityCurve Transition(long begin, double from, double to, FadeProfile profile)
    {
        double delta = to - from;
        double duration = delta >= 0 ? profile.TurnOnMilliseconds * delta : profile.TurnOffMilliseconds * -delta;
        return Ramp(begin, from, to, duration);
    }

    /// <summary>Holds <paramref name="from"/> for <paramref name="delayMilliseconds"/>, then eases out to <paramref name="to"/> (power-up rises).</summary>
    public static OpacityCurve DelayedEaseOut(long begin, double from, double to, double delayMilliseconds, double durationMilliseconds)
    {
        var builder = new OpacityCurveBuilder(begin);
        if (delayMilliseconds > 0)
        {
            builder.Hold(from, delayMilliseconds / 1000);
        }

        return builder.EaseOut(from, to, durationMilliseconds / 1000).End(to);
    }

    /// <summary>
    /// The continuous raised cosine <c>b = 0.5 - 0.5 cos(2 pi (x / period - offset))</c> of Slow Glow, Waves and the idle Dance,
    /// anchored to the clock epoch: one period of cubic Hermite pieces split at the quarter points, repeated forever.
    /// </summary>
    public static OpacityCurve RaisedCosine(BrightnessWave wave, WaveTiming timing)
    {
        var builder = new OpacityCurveBuilder(timing.EpochTimestamp);
        AppendRaisedCosinePeriod(builder, wave, timing, PhaseAt(wave, timing, timing.EpochTimestamp));
        return builder.RepeatFrom(0);
    }

    /// <summary>
    /// The raised cosine reached smoothly from <paramref name="fromValue"/>: a Hermite lead-in of
    /// <paramref name="leadInMilliseconds"/> from <paramref name="now"/> into the wave (no jump when a bulb that was lit, for
    /// example after the power-up, starts to glow slowly), then the wave, in phase with the clock, forever.
    /// </summary>
    public static OpacityCurve RaisedCosineFrom(BrightnessWave wave, WaveTiming timing, long now, double fromValue, double leadInMilliseconds)
    {
        double periodSeconds = PeriodSeconds(wave, timing);
        double leadIn = Math.Min(leadInMilliseconds / 1000, periodSeconds / 4);
        long joinAt = now + (long)(leadIn * Stopwatch.Frequency);
        double joinPhase = PhaseAt(wave, timing, joinAt);
        var builder = new OpacityCurveBuilder(now);
        builder.Hermite(fromValue, RaisedCosineValue(joinPhase), 0, RaisedCosineSlope(joinPhase) / periodSeconds, leadIn);
        double repeatFrom = builder.Cursor;
        AppendRaisedCosinePeriod(builder, wave, timing, joinPhase);
        return builder.RepeatFrom(repeatFrom);
    }

    /// <summary>
    /// The same wave without Smooth Fading: lit (1) while the raised cosine is at least 0.5, else unlit (0) (PRODUCT-SPEC 5.8,
    /// "continuous patterns show lit when b &gt;= 0.5").
    /// </summary>
    public static OpacityCurve SquareWave(BrightnessWave wave, WaveTiming timing)
    {
        double periodSeconds = PeriodSeconds(wave, timing);
        var builder = new OpacityCurveBuilder(timing.EpochTimestamp);
        foreach ((double from, double to) in PhasePieces(PhaseAt(wave, timing, timing.EpochTimestamp), quarterBoundaries: false))
        {
            double middle = (from + to) / 2;
            double fraction = middle - Math.Floor(middle);
            builder.Hold(fraction is >= 0.25 and < 0.75 ? 1 : 0, (to - from) * periodSeconds);
        }

        return builder.RepeatFrom(0);
    }

    /// <summary>
    /// A Dance group envelope (PRODUCT-SPEC 5.10): from <paramref name="valueNow"/> at <paramref name="now"/> to the group's
    /// <see cref="DanceGroupUpdate.StartBrightness"/> when the sound is heard, a 30 ms linear attack to the peak, then the
    /// exponential decay (tau 250 ms) as cubic Hermite pieces, cut to 0 after six time constants. Matches
    /// <see cref="DanceEnvelope.BrightnessAt"/>.
    /// </summary>
    public static OpacityCurve DanceGroup(DanceGroupUpdate update, long now, double valueNow)
    {
        long begin = Math.Min(now, update.Timestamp);
        var builder = new OpacityCurveBuilder(begin);
        if (update.Timestamp > now)
        {
            builder.Linear(valueNow, update.StartBrightness, (update.Timestamp - now) / (double)Stopwatch.Frequency);
        }

        builder.Linear(update.StartBrightness, update.PeakBrightness, DanceEnvelope.AttackMilliseconds / 1000);
        double peak = update.PeakBrightness;
        double tauSeconds = DanceEnvelope.DecayTauMilliseconds / 1000;
        for (double t = 0; t < DecayLengthMilliseconds; t += DecayPieceMilliseconds)
        {
            double t0 = t / 1000;
            double t1 = (t + DecayPieceMilliseconds) / 1000;
            double v0 = peak * Math.Exp(-t0 / tauSeconds);
            double v1 = peak * Math.Exp(-t1 / tauSeconds);
            builder.Hermite(v0, v1, -v0 / tauSeconds, -v1 / tauSeconds, t1 - t0);
        }

        return builder.End(0);
    }

    /// <summary>The raised cosine at phase <paramref name="u"/> (periods).</summary>
    internal static double RaisedCosineValue(double u) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * u);

    private static double RaisedCosineSlope(double u) => Math.PI * Math.Sin(2 * Math.PI * u);

    private static double PeriodSeconds(BrightnessWave wave, WaveTiming timing) => wave.PeriodSteps * timing.StepPeriodMilliseconds / 1000;

    /// <summary>The wave's phase (fraction of a period, 0-1) at a timestamp.</summary>
    private static double PhaseAt(BrightnessWave wave, WaveTiming timing, long timestamp)
    {
        double steps = timing.EpochStep + (timestamp - timing.EpochTimestamp) / (double)Stopwatch.Frequency * 1000 / timing.StepPeriodMilliseconds;
        double u = steps / wave.PeriodSteps - wave.PhaseOffset;
        return u - Math.Floor(u);
    }

    private static void AppendRaisedCosinePeriod(OpacityCurveBuilder builder, BrightnessWave wave, WaveTiming timing, double phase)
    {
        double periodSeconds = PeriodSeconds(wave, timing);
        foreach ((double from, double to) in PhasePieces(phase, quarterBoundaries: true))
        {
            builder.Hermite(
                RaisedCosineValue(from),
                RaisedCosineValue(to),
                RaisedCosineSlope(from) / periodSeconds,
                RaisedCosineSlope(to) / periodSeconds,
                (to - from) * periodSeconds);
        }
    }

    /// <summary>Splits one period starting at <paramref name="phase"/> at the quarter points (or at 0.25 and 0.75 only).</summary>
    private static IEnumerable<(double From, double To)> PhasePieces(double phase, bool quarterBoundaries)
    {
        double[] marks = quarterBoundaries ? [0, 0.25, 0.5, 0.75] : [0.25, 0.75];
        var cuts = new List<double> { phase };
        for (double cycle = Math.Floor(phase); cycle <= Math.Floor(phase) + 1; cycle++)
        {
            foreach (double mark in marks)
            {
                double cut = cycle + mark;
                if (cut > phase + 1e-9 && cut < phase + 1 - 1e-9)
                {
                    cuts.Add(cut);
                }
            }
        }

        cuts.Sort();
        cuts.Add(phase + 1);
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            yield return (cuts[i], cuts[i + 1]);
        }
    }
}
