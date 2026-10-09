using HolidayLights.Rendering.Composition;

namespace HolidayLights.Rendering.Animation;

/// <summary>
/// Turns a flash sequencer into visual changes (PRODUCT-SPEC 5.5-5.10, D21). Light bulbs of the classic patterns get one
/// repeating animation of their strip's period (<see cref="PeriodicBrightness"/>), continuous patterns a repeating raised
/// cosine, Dance the group envelopes created from music events; these need no work per step. Otherwise, at each step the
/// animator moves the sequencer, compares the target states with what is shown and sends only the differences: frame
/// switches for animation bulbs and opacity fades with the pattern's fade profile for light bulbs. Curves shared by many
/// bulbs are created once. Lights thread only.
/// </summary>
internal sealed class BulbAnimator
{
    /// <summary>Lead-in when a bulb joins a continuous wave from another brightness (after the power-up, Dance going idle).</summary>
    private const double WaveLeadInMilliseconds = 600;

    private readonly IFlashSequencer sequencer;
    private readonly FlashBulbInfo[] facts;
    private readonly BulbVisualState[] shown;
    private readonly Source[] sources;
    private readonly BrightnessWave?[] boundWave;
    private readonly OpacityCurve[] litCurves;

    /// <summary>
    /// Light bulbs whose visuals were taken over from what they showed (<see cref="SeedShown"/>): they may still run an
    /// animation bound by a previous animator (a repeating period, a wave), so the next step binds them even when their
    /// state does not change.
    /// </summary>
    private readonly bool[] adopted;
    private readonly PeriodicBrightness periodic;
    private readonly bool stepsChangeBulbs;
    private readonly OpacityCurve?[] groupCurves = new OpacityCurve?[DanceEnvelope.GroupCount];
    private readonly Dictionary<BrightnessWave, (OpacityCurve Lit, OpacityCurve Glow)> waveCurves = [];
    private readonly Dictionary<(int Class, double From), (OpacityCurve Lit, OpacityCurve Glow)> periodCurves = [];
    private WaveTiming waveTiming;
    private bool running;
    private int waitingForPeriod;

    /// <summary>Creates the animator for a sequencer (its <see cref="IFlashSequencer.Current"/> is not shown yet).</summary>
    /// <param name="sequencer">The sequencer (owned by the Lights thread from now on).</param>
    /// <param name="glowIntensity">The glow intensity (<see cref="GlowLevels.Intensity"/>).</param>
    public BulbAnimator(IFlashSequencer sequencer, float glowIntensity)
    {
        this.sequencer = sequencer;
        GlowIntensity = glowIntensity;
        int count = sequencer.Layout.Placements.Count;
        facts = new FlashBulbInfo[count];
        shown = new BulbVisualState[count];
        sources = new Source[count];
        boundWave = new BrightnessWave?[count];
        litCurves = new OpacityCurve[count];
        adopted = new bool[count];
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            facts[ordinal] = sequencer.GetBulb(ordinal);
            litCurves[ordinal] = OpacityCurve.Constant(0);
        }

        periodic = PeriodicBrightness.Learn(sequencer, facts);
        waitingForPeriod = periodic.Count;
        bool flashing = sequencer.Options.Pattern != FlashPatternId.DontFlash && !sequencer.Options.StopFlashing;
        stepsChangeBulbs = flashing && Enumerable.Range(0, count).Any(ordinal =>
            facts[ordinal].Kind == BulbAnimationKind.Animation || (facts[ordinal].Kind == BulbAnimationKind.LightBulb && !periodic.IsPeriodic(ordinal)));
    }

    /// <summary>Where a light bulb's brightness comes from.</summary>
    private enum Source
    {
        /// <summary>The sequencer's discrete state at each step (with fades).</summary>
        Discrete,

        /// <summary>One repeating animation of its strip's period (classic patterns).</summary>
        Periodic,

        /// <summary>A repeating wave (Slow Glow, Waves, the idle Dance).</summary>
        Wave,

        /// <summary>Its Dance group's envelope.</summary>
        Music,
    }

    /// <summary>The glow intensity applied to glow visuals.</summary>
    public float GlowIntensity { get; private set; }

    /// <summary>
    /// True when the Lights thread has to process steps: an animation bulb or a light bulb without a repeating animation can
    /// change (not Don't Flash, not "Stop Flashing"), or a light bulb of a classic pattern still waits for its period.
    /// </summary>
    public bool NeedsSteps => stepsChangeBulbs || waitingForPeriod > 0;

    /// <summary>True for "Dance to the Music" (music events are read only then).</summary>
    public bool IsDancePattern => sequencer.Options.Pattern == FlashPatternId.DanceToMusic;

    /// <summary>Light bulbs that follow one repeating animation of their strip's period (no work per step).</summary>
    public int PeriodicLightBulbs => periodic.Count;

    /// <summary>Smooth Fading, which "Limit Flashing to 3 Flashes per Second" forces on (PRODUCT-SPEC 5.5.3).</summary>
    private bool SmoothFading => sequencer.Options.SmoothFading || sequencer.Options.LimitFlashing;

    /// <summary>Changes the glow intensity (Soft/Bright) and re-applies every bulb.</summary>
    public void SetGlowIntensity(float intensity, long now, WaveTiming timing, IBulbVisualTarget target)
    {
        GlowIntensity = intensity;
        waveCurves.Clear();
        ApplyFullState(Enumerable.Range(0, facts.Length), now, timing, target);
    }

    /// <summary>
    /// Sends the complete current state of some bulbs (a display shown again, a new glow intensity). Light bulbs of a classic
    /// pattern show the period of the step at <paramref name="now"/> once the pattern runs, and the sequencer's state before
    /// (during a power-up).
    /// </summary>
    public void ApplyFullState(IEnumerable<int> ordinals, long now, WaveTiming timing, IBulbVisualTarget target)
    {
        UseTiming(timing);
        periodCurves.Clear();
        ReadOnlySpan<BulbVisualState> current = sequencer.Current;
        foreach (int ordinal in ordinals)
        {
            BulbVisualState state = current[ordinal];
            shown[ordinal] = state;
            if (facts[ordinal].Kind != BulbAnimationKind.LightBulb)
            {
                target.ShowFrame(ordinal, state.Frame);
                continue;
            }

            Source source = SourceOf(ordinal, out BrightnessWave? wave);
            if (source == Source.Periodic && !running)
            {
                source = Source.Discrete;
            }

            SetSource(ordinal, source, wave);
            switch (source)
            {
                case Source.Periodic:
                    long step = timing.StepAt(now);
                    BindPeriodic(ordinal, step, timing.TimestampOfStep(step), periodic.ValueAt(periodic.ClassOf(ordinal), step), target);
                    break;
                case Source.Wave:
                    (OpacityCurve lit, OpacityCurve glow) = WaveCurves(wave!.Value);
                    Bind(ordinal, lit, glow, target);
                    break;
                case Source.Music when groupCurves[facts[ordinal].MusicGroup] is { } envelope:
                    Bind(ordinal, envelope, envelope.Scale(GlowIntensity), target);
                    break;
                default:
                    Bind(ordinal, OpacityCurve.Constant(state.Brightness), OpacityCurve.Constant(state.Glow * GlowIntensity), target);
                    break;
            }
        }
    }

    /// <summary>
    /// Plays the power-up wave (PRODUCT-SPEC 4.3.2): light bulbs start unlit and rise to lit (120 ms, glow 300 ms) when the
    /// wave reaches them; other bulbs show frame 0 at 40 % and rise to full. Then <see cref="AssumeAllLit"/> and step 0.
    /// </summary>
    /// <param name="plan">The timeline.</param>
    /// <param name="begin">When the curves begin (now).</param>
    /// <param name="delayMilliseconds">When the power-up starts after <paramref name="begin"/> (the theme transition's fade-out).</param>
    /// <param name="ordinals">The bulbs on displays that are not resting.</param>
    /// <param name="target">The visuals.</param>
    public void ApplyPowerUp(PowerUpPlan plan, long begin, double delayMilliseconds, IEnumerable<int> ordinals, IBulbVisualTarget target)
    {
        running = false;
        foreach (int ordinal in ordinals)
        {
            double fullAt = delayMilliseconds + plan.FullBrightnessAt(ordinal);
            if (facts[ordinal].Kind == BulbAnimationKind.LightBulb)
            {
                OpacityCurve lit = OpacityCurves.DelayedEaseOut(begin, 0, 1, fullAt - MotionTimings.PowerUpRise, MotionTimings.PowerUpRise);
                OpacityCurve glow = OpacityCurves.DelayedEaseOut(begin, 0, GlowIntensity, fullAt - MotionTimings.PowerUpGlowRise, MotionTimings.PowerUpGlowRise);
                Bind(ordinal, lit, glow, target);
                SetSource(ordinal, Source.Discrete, null);
            }
            else
            {
                target.ShowFrame(ordinal, 0);
                target.SetOpacity(ordinal, OpacityCurves.DelayedEaseOut(
                    begin, MotionTimings.PowerUpDimOpacity, 1, fullAt - MotionTimings.PowerUpRise, MotionTimings.PowerUpRise));
            }
        }
    }

    /// <summary>Records that every light bulb shows lit with full glow and every other bulb frame 0 (the end of the power-up).</summary>
    public void AssumeAllLit()
    {
        for (int ordinal = 0; ordinal < facts.Length; ordinal++)
        {
            bool light = facts[ordinal].Kind == BulbAnimationKind.LightBulb;
            shown[ordinal] = new BulbVisualState(light ? facts[ordinal].LitFrame : 0, 1f, light ? 1f : 0f);
            litCurves[ordinal] = OpacityCurve.Constant(light ? 1f : 0f);
            SetSource(ordinal, Source.Discrete, null);
        }
    }

    /// <summary>
    /// Takes over what existing visuals show (a new sequencer for visuals that stay, new visuals, a restarted pattern): the
    /// next <see cref="Step"/> then fades from those values instead of jumping, and binds every light bulb it took over, since
    /// its visuals may still run another pattern's animation. Bulbs without a value keep their state.
    /// </summary>
    public void SeedShown(Func<int, BulbVisualState?> current)
    {
        for (int ordinal = 0; ordinal < facts.Length; ordinal++)
        {
            if (current(ordinal) is { } state)
            {
                shown[ordinal] = state;
                litCurves[ordinal] = OpacityCurve.Constant(state.Brightness);
                adopted[ordinal] = facts[ordinal].Kind == BulbAnimationKind.LightBulb;
                SetSource(ordinal, Source.Discrete, null);
            }
        }
    }

    /// <summary>
    /// Evaluates a step and sends what changed. Fades start at <paramref name="timestamp"/>: the step boundary, or now when the
    /// pattern starts or continues on other visuals. Light bulbs of a classic pattern get their repeating animation here.
    /// </summary>
    public void Step(long step, long timestamp, WaveTiming timing, IBulbVisualTarget target)
    {
        UseTiming(timing);
        running = true;
        periodCurves.Clear();
        sequencer.MoveTo(step, timestamp);
        ReadOnlySpan<BulbVisualState> current = sequencer.Current;
        FadeProfile profile = sequencer.GetFadeProfile(timing.StepPeriodMilliseconds);
        var transitions = new Dictionary<(float From, float To, bool Glow), OpacityCurve>();
        for (int ordinal = 0; ordinal < facts.Length; ordinal++)
        {
            BulbVisualState state = current[ordinal];
            if (facts[ordinal].Kind != BulbAnimationKind.LightBulb)
            {
                if (state.Frame != shown[ordinal].Frame)
                {
                    target.ShowFrame(ordinal, state.Frame);
                }
            }
            else
            {
                UpdateLightBulb(ordinal, state, timestamp, profile, target, transitions);
            }

            shown[ordinal] = state;
        }
    }

    /// <summary>Feeds a batch of music events (Dance) and sends the new group envelopes, frames and source changes.</summary>
    public void ApplyMusic(ReadOnlySpan<MusicEvent> events, long now, WaveTiming timing, IBulbVisualTarget target)
    {
        UseTiming(timing);
        MusicResponse response = sequencer.ApplyMusicEvents(events);
        foreach (DanceGroupUpdate update in response.Groups)
        {
            if (update.Group is >= 0 and < DanceEnvelope.GroupCount)
            {
                double valueNow = groupCurves[update.Group]?.Evaluate(now) ?? update.StartBrightness;
                groupCurves[update.Group] = OpacityCurves.DanceGroup(update, now, valueNow);
            }
        }

        if (response.ModeChanged || response.Groups.Count > 0)
        {
            var updated = new HashSet<int>(response.Groups.Select(g => g.Group));
            for (int ordinal = 0; ordinal < facts.Length; ordinal++)
            {
                if (facts[ordinal].Kind != BulbAnimationKind.LightBulb)
                {
                    continue;
                }

                Source source = SourceOf(ordinal, out _);
                if (source != sources[ordinal] || (source == Source.Music && updated.Contains(facts[ordinal].MusicGroup)))
                {
                    Rebind(ordinal, now, target);
                }
            }
        }

        // A beat moves the animation bulbs; a mode change hands them back to the step frames of the idle pattern (or takes
        // them over from there), so both re-read the frames at once instead of at the next step.
        if (response.FramesChanged || response.ModeChanged)
        {
            ReadOnlySpan<BulbVisualState> current = sequencer.Current;
            for (int ordinal = 0; ordinal < facts.Length; ordinal++)
            {
                if (facts[ordinal].Kind != BulbAnimationKind.LightBulb && current[ordinal].Frame != shown[ordinal].Frame)
                {
                    target.ShowFrame(ordinal, current[ordinal].Frame);
                    shown[ordinal] = current[ordinal];
                }
            }
        }
    }

    /// <summary>
    /// The step period changed at <paramref name="timing"/>'s epoch (a step boundary): re-anchors every repeating animation,
    /// waves and the periods of classic patterns (whose fades follow the step period), to the new clock segment.
    /// </summary>
    public void ChangeSpeed(WaveTiming timing, IBulbVisualTarget target)
    {
        UseTiming(timing);
        periodCurves.Clear();
        long step = timing.EpochStep;
        for (int ordinal = 0; ordinal < facts.Length; ordinal++)
        {
            if (sources[ordinal] == Source.Wave && boundWave[ordinal] is { } wave)
            {
                (OpacityCurve lit, OpacityCurve glow) = WaveCurves(wave);
                Bind(ordinal, lit, glow, target);
            }
            else if (sources[ordinal] == Source.Periodic)
            {
                BindPeriodic(ordinal, step, timing.EpochTimestamp, periodic.ValueAt(periodic.ClassOf(ordinal), step - 1), target);
            }
        }
    }

    /// <summary>
    /// Creates every repeating animation again with the same picture, in phase with <paramref name="timing"/>, so that none
    /// keeps running in DWM for days while the lights stay on.
    /// </summary>
    public void RefreshRepeating(long now, WaveTiming timing, IBulbVisualTarget target)
    {
        UseTiming(timing);
        periodCurves.Clear();
        long step = timing.StepAt(now);
        for (int ordinal = 0; ordinal < facts.Length; ordinal++)
        {
            if (sources[ordinal] == Source.Wave && boundWave[ordinal] is { } wave)
            {
                (OpacityCurve lit, OpacityCurve glow) = WaveCurves(wave);
                Bind(ordinal, lit, glow, target);
            }
            else if (sources[ordinal] == Source.Periodic)
            {
                BindPeriodic(ordinal, step, timing.TimestampOfStep(step), periodic.ValueAt(periodic.ClassOf(ordinal), step - 1), target);
            }
        }
    }

    private void UpdateLightBulb(
        int ordinal, BulbVisualState state, long timestamp, FadeProfile profile, IBulbVisualTarget target, Dictionary<(float, float, bool), OpacityCurve> transitions)
    {
        Source source = SourceOf(ordinal, out BrightnessWave? wave);
        if (source != sources[ordinal] || (source == Source.Wave && wave != boundWave[ordinal]))
        {
            Rebind(ordinal, timestamp, target);
            return;
        }

        if (source != Source.Discrete)
        {
            return;
        }

        BulbVisualState previous = shown[ordinal];
        if (adopted[ordinal] || state.Brightness != previous.Brightness || state.Glow != previous.Glow)
        {
            OpacityCurve lit = Transition(previous.Brightness, state.Brightness, glow: false, timestamp, profile, transitions);
            OpacityCurve glow = Transition(previous.Glow, state.Glow, glow: true, timestamp, profile, transitions);
            Bind(ordinal, lit, glow, target);
        }
    }

    /// <summary>Which source the sequencer asks for now.</summary>
    private Source SourceOf(int ordinal, out BrightnessWave? wave)
    {
        wave = sequencer.GetWave(ordinal);
        if (wave is not null)
        {
            return Source.Wave;
        }

        if (sequencer.IsDancing && facts[ordinal].MusicGroup >= 0)
        {
            return Source.Music;
        }

        return periodic.IsPeriodic(ordinal) ? Source.Periodic : Source.Discrete;
    }

    /// <summary>Records a light bulb's source and counts the bulbs that still wait for their period.</summary>
    private void SetSource(int ordinal, Source source, BrightnessWave? wave)
    {
        if (periodic.IsPeriodic(ordinal) && (sources[ordinal] == Source.Periodic) != (source == Source.Periodic))
        {
            waitingForPeriod += source == Source.Periodic ? -1 : 1;
        }

        sources[ordinal] = source;
        boundWave[ordinal] = wave;
    }

    /// <summary>Moves a light bulb to the source the sequencer asks for now, smoothly from what it shows.</summary>
    private void Rebind(int ordinal, long now, IBulbVisualTarget target)
    {
        Source source = SourceOf(ordinal, out BrightnessWave? wave);
        double from = litCurves[ordinal].Evaluate(now);
        SetSource(ordinal, source, wave);
        OpacityCurve lit;
        switch (source)
        {
            case Source.Periodic:
                BindPeriodic(ordinal, sequencer.Step, now, from, target);
                return;
            case Source.Wave:
                lit = SmoothFading
                    ? OpacityCurves.RaisedCosineFrom(wave!.Value, waveTiming, now, from, WaveLeadInMilliseconds)
                    : OpacityCurves.SquareWave(wave!.Value, waveTiming);
                break;
            case Source.Music:
                // The group's envelope, or a gentle fall to the group's brightness until its first note.
                lit = groupCurves[facts[ordinal].MusicGroup]
                    ?? OpacityCurves.Ramp(now, from, sequencer.Current[ordinal].Brightness, DanceEnvelope.DecayTauMilliseconds);
                break;
            default:
                FadeProfile profile = sequencer.GetFadeProfile(waveTiming.StepPeriodMilliseconds);
                lit = OpacityCurves.Transition(now, from, sequencer.Current[ordinal].Brightness, profile);
                break;
        }

        Bind(ordinal, lit, lit.Scale(GlowIntensity), target);
    }

    /// <summary>
    /// Binds a light bulb's repeating period from <paramref name="begin"/> within <paramref name="step"/>; bulbs of one class
    /// that start from the same value share the curves (the cache holds for one call of a public method).
    /// </summary>
    private void BindPeriodic(int ordinal, long step, long begin, double from, IBulbVisualTarget target)
    {
        int classId = periodic.ClassOf(ordinal);
        if (!periodCurves.TryGetValue((classId, from), out (OpacityCurve Lit, OpacityCurve Glow) curves))
        {
            FadeProfile profile = sequencer.GetFadeProfile(waveTiming.StepPeriodMilliseconds);
            OpacityCurve lit = periodic.Curve(classId, waveTiming, step, begin, from, profile);
            curves = (lit, lit.Scale(GlowIntensity));
            periodCurves[(classId, from)] = curves;
        }

        Bind(ordinal, curves.Lit, curves.Glow, target);
    }

    private OpacityCurve Transition(float from, float to, bool glow, long timestamp, FadeProfile profile, Dictionary<(float, float, bool), OpacityCurve> transitions)
    {
        if (!transitions.TryGetValue((from, to, glow), out OpacityCurve? curve))
        {
            curve = OpacityCurves.Transition(timestamp, from, to, profile);
            if (glow)
            {
                curve = curve.Scale(GlowIntensity);
            }

            transitions[(from, to, glow)] = curve;
        }

        return curve;
    }

    private (OpacityCurve Lit, OpacityCurve Glow) WaveCurves(BrightnessWave wave)
    {
        if (!waveCurves.TryGetValue(wave, out (OpacityCurve Lit, OpacityCurve Glow) curves))
        {
            OpacityCurve lit = SmoothFading ? OpacityCurves.RaisedCosine(wave, waveTiming) : OpacityCurves.SquareWave(wave, waveTiming);
            curves = (lit, lit.Scale(GlowIntensity));
            waveCurves[wave] = curves;
        }

        return curves;
    }

    private void UseTiming(WaveTiming timing)
    {
        if (timing != waveTiming)
        {
            waveTiming = timing;
            waveCurves.Clear();
        }
    }

    private void Bind(int ordinal, OpacityCurve lit, OpacityCurve glow, IBulbVisualTarget target)
    {
        adopted[ordinal] = false;
        litCurves[ordinal] = lit;
        target.SetLit(ordinal, lit);
        target.SetGlow(ordinal, glow);
    }
}
