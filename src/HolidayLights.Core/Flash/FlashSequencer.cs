using System.Diagnostics;

namespace HolidayLights.Core.Flash;

/// <summary>
/// The state machine of one flash pattern over one layout (<see cref="IFlashSequencer"/>; PRODUCT-SPEC 5.6-5.10).
/// </summary>
/// <remarks>
/// <para>Discrete states come from <see cref="MoveTo"/>: one step forward updates incrementally (no allocation); any other
/// step is evaluated directly, so a sequencer can join a clock that has run for days.</para>
/// <para>Smooth fading is on when <see cref="FlashOptions.SmoothFading"/> is, and always while
/// <see cref="FlashOptions.LimitFlashing"/> is on (PRODUCT-SPEC 5.5.3). Combination plays its patterns from
/// <see cref="CombinationSchedule"/>; its waves and fades change with the segment of the current step.</para>
/// <para><see cref="Sample"/> moves the sequencer to the sampled step, like <see cref="MoveTo"/>. Not thread-safe: one
/// thread at a time.</para>
/// </remarks>
internal sealed class FlashSequencer : IFlashSequencer
{
    private readonly FlashBulbTable bulbs;
    private readonly FlashBulbInfo[] infos;
    private readonly RandomFlashingTable? random;
    private readonly TwinkleModel? twinkle;
    private readonly DanceModel? dance;
    private readonly bool fading;
    // Scratch values shared by every light bulb of one evaluation: wave values (Sample) and Dance group levels (each
    // evaluation fills and reads them before the next one starts).
    private readonly float[] sampledWaves = new float[(int)NewPatterns.WavePeriodSteps];
    private readonly float[] groupLevels = new float[DanceEnvelope.GroupCount];
    private BulbVisualState[] current;
    private BulbVisualState[] previous;

    /// <summary>Builds the sequencer at step 0.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="resolver">Resolves its bulbs.</param>
    /// <param name="options">Pattern and seeds.</param>
    public FlashSequencer(LightsLayout layout, IBulbResolver resolver, FlashOptions options)
    {
        Layout = layout;
        Options = options;
        bulbs = FlashBulbTable.Build(layout, resolver);
        fading = options.SmoothFading || options.LimitFlashing;
        FlashPatternId pattern = options.Pattern;
        if (pattern is FlashPatternId.RandomFlashing or FlashPatternId.Combination)
        {
            random = new RandomFlashingTable(bulbs, options.ClassicRandomSeed);
        }

        if (pattern is FlashPatternId.Twinkle or FlashPatternId.Combination)
        {
            twinkle = new TwinkleModel(bulbs, options.PatternSeed);
        }

        if (pattern == FlashPatternId.DanceToMusic)
        {
            dance = new DanceModel(bulbs, options.LimitFlashing);
        }

        infos = new FlashBulbInfo[bulbs.Count];
        for (int o = 0; o < infos.Length; o++)
        {
            infos[o] = new FlashBulbInfo(
                o, bulbs.Kinds[o], bulbs.FrameCounts[o], bulbs.LitFrames[o], bulbs.StripFrameCount(o, pattern),
                bulbs.MusicGroups[o], bulbs.LightIndices[o], bulbs.AnimationIndices[o]);
        }

        current = new BulbVisualState[bulbs.Count];
        previous = new BulbVisualState[bulbs.Count];
        Evaluate(0, 0, current);
        current.CopyTo(previous, 0);
    }

    /// <inheritdoc />
    public LightsLayout Layout { get; }

    /// <inheritdoc />
    public FlashOptions Options { get; }

    /// <inheritdoc />
    public long Step { get; private set; }

    /// <inheritdoc />
    public ReadOnlySpan<BulbVisualState> Current => current;

    /// <inheritdoc />
    public bool IsDancing => dance is { IsPlaying: true } && !Options.StopFlashing;

    /// <inheritdoc />
    public FlashBulbInfo GetBulb(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, infos.Length);
        return infos[ordinal];
    }

    /// <inheritdoc />
    /// <remarks>Steps before 0 show step 0. "Stop Flashing" ignores the step.</remarks>
    public void MoveTo(long step, long timestamp)
    {
        step = Math.Max(0, step);
        if (Options.StopFlashing || step == Step)
        {
            return;
        }

        dance?.OnSteps(step - Step, timestamp);
        if (step == Step + 1)
        {
            (previous, current) = (current, previous);
            Evaluate(step, timestamp, current);
        }
        else if (step == 0)
        {
            Evaluate(0, timestamp, current);
            current.CopyTo(previous, 0);
        }
        else
        {
            Evaluate(step - 1, timestamp, previous);
            Evaluate(step, timestamp, current);
        }

        Step = step;
    }

    /// <inheritdoc />
    public FadeProfile GetFadeProfile(double stepPeriodMilliseconds) =>
        Options.StopFlashing || !fading ? FadeProfile.Instant : FadeTiming.For(PatternAt(Step).Pattern, stepPeriodMilliseconds);

    /// <inheritdoc />
    public BrightnessWave? GetWave(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(ordinal, infos.Length);
        if (Options.StopFlashing || !fading || bulbs.Kinds[ordinal] != BulbAnimationKind.LightBulb)
        {
            return null;
        }

        (FlashPatternId pattern, long segmentStart) = PatternAt(Step);
        return pattern switch
        {
            FlashPatternId.SlowGlow => NewPatterns.SlowGlow(segmentStart),
            FlashPatternId.Waves => NewPatterns.Waves(bulbs.LightIndices[ordinal], segmentStart),
            FlashPatternId.DanceToMusic when !IsDancing => NewPatterns.SlowGlow(0),
            _ => null,
        };
    }

    /// <inheritdoc />
    /// <remarks>Afterwards <see cref="Current"/> shows the levels the batch reaches (its last event plus the attack).</remarks>
    public MusicResponse ApplyMusicEvents(ReadOnlySpan<MusicEvent> events)
    {
        if (dance is null || Options.StopFlashing || events.IsEmpty)
        {
            return MusicResponse.None;
        }

        double idle = NewPatterns.SlowGlow(0).Evaluate(Step + 0.5);
        MusicResponse response = dance.Apply(events, Step, idle);
        if (!ReferenceEquals(response, MusicResponse.None))
        {
            long attack = (long)Math.Round(DanceEnvelope.AttackMilliseconds * Stopwatch.Frequency / 1000.0);
            Evaluate(Step, events[^1].Timestamp + attack, current);
        }

        return response;
    }

    /// <inheritdoc />
    public void Sample(long timestamp, StepClock clock, Span<BulbVisualState> destination)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (destination.Length < current.Length)
        {
            throw new ArgumentException("The destination needs one state per placement.", nameof(destination));
        }

        if (Options.StopFlashing)
        {
            current.CopyTo(destination);
            return;
        }

        double position = clock.PositionAt(timestamp);
        long step = position > 0 ? (long)Math.Floor(position) : 0;
        long stepStart = clock.TimestampOfStep(step);
        MoveTo(step, stepStart);

        double elapsed = Math.Max(0, Milliseconds(timestamp - stepStart));
        double period = Milliseconds(clock.TimestampOfStep(step + 1) - stepStart);
        (FlashPatternId pattern, long segmentStart) = PatternAt(step);
        LightSource source = PrepareLightSource(pattern, segmentStart, Math.Max(position, 0), timestamp);
        FadeProfile profile = fading ? FadeTiming.For(pattern, period) : FadeProfile.Instant;
        int[] groups = bulbs.MusicGroups;
        int[] lightIndices = bulbs.LightIndices;
        for (int o = 0; o < current.Length; o++)
        {
            if (bulbs.Kinds[o] != BulbAnimationKind.LightBulb)
            {
                destination[o] = current[o];
                continue;
            }

            float brightness = source switch
            {
                LightSource.Music => groupLevels[groups[o]],
                LightSource.Waves => sampledWaves[lightIndices[o] % sampledWaves.Length],
                LightSource.SlowGlow => sampledWaves[0],
                _ => FadeTiming.Resolve(previous[o].Brightness, current[o].Brightness, elapsed, profile),
            };
            destination[o] = bulbs.ShowBrightness(o, brightness);
        }
    }

    private static double Milliseconds(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

    private (FlashPatternId Pattern, long SegmentStart) PatternAt(long step) =>
        Options.Pattern == FlashPatternId.Combination ? CombinationSchedule.At(step) : (Options.Pattern, 0);

    private void Evaluate(long step, long timestamp, Span<BulbVisualState> states)
    {
        if (Options.StopFlashing)
        {
            for (int o = 0; o < states.Length; o++)
            {
                states[o] = bulbs.Steady(o);
            }

            return;
        }

        (FlashPatternId pattern, long segmentStart) = PatternAt(step);
        long local = step - segmentStart;
        switch (pattern)
        {
            case FlashPatternId.Twinkle:
                twinkle!.Evaluate(local, states);
                break;
            case FlashPatternId.SlowGlow:
                NewPatterns.EvaluateContinuous(waves: false, step, segmentStart, fading, bulbs, states);
                break;
            case FlashPatternId.Waves:
                NewPatterns.EvaluateContinuous(waves: true, step, segmentStart, fading, bulbs, states);
                break;
            case FlashPatternId.ChaseAround:
                NewPatterns.EvaluateChaseAround(local, bulbs, states);
                break;
            case FlashPatternId.DanceToMusic when dance!.IsPlaying:
                EvaluateDance(timestamp, states);
                break;
            case FlashPatternId.DanceToMusic:
                NewPatterns.EvaluateContinuous(waves: false, step, 0, fading, bulbs, states);
                break;
            default:
                ClassicPatterns.Evaluate(pattern, local, bulbs, random, states);
                break;
        }
    }

    private void EvaluateDance(long timestamp, Span<BulbVisualState> states)
    {
        for (int g = 0; g < groupLevels.Length; g++)
        {
            groupLevels[g] = DanceBrightness(g, timestamp);
        }

        for (int o = 0; o < states.Length; o++)
        {
            switch (bulbs.Kinds[o])
            {
                case BulbAnimationKind.LightBulb:
                    states[o] = bulbs.ShowBrightness(o, groupLevels[bulbs.MusicGroups[o]]);
                    break;
                case BulbAnimationKind.Animation:
                    states[o] = bulbs.ShowFrame(o, (int)NewPatterns.Mod(dance!.FrameCounter, bulbs.FrameCounts[o]));
                    break;
                default:
                    states[o] = bulbs.ShowFrame(o, 0);
                    break;
            }
        }
    }

    // Without Smooth Fading the music shows as lit or dark: lit while the group's brightness is 0.5 or more.
    private float DanceBrightness(int group, long timestamp)
    {
        float brightness = dance!.Brightness(group, timestamp);
        return fading ? brightness : brightness >= 0.5f ? 1f : 0f;
    }

    // The brightness every light bulb shares at one moment is computed once per sample: Slow Glow's value, the 16 phases of
    // Waves (bulb q uses q mod 16) or the 13 Dance groups. Without Smooth Fading the continuous patterns hold their step
    // value, which the fade of a discrete pattern reproduces with instant durations.
    private LightSource PrepareLightSource(FlashPatternId pattern, long segmentStart, double position, long timestamp)
    {
        if (pattern == FlashPatternId.DanceToMusic && IsDancing)
        {
            for (int g = 0; g < groupLevels.Length; g++)
            {
                groupLevels[g] = DanceBrightness(g, timestamp);
            }

            return LightSource.Music;
        }

        if (!fading)
        {
            return LightSource.Steps;
        }

        if (pattern is FlashPatternId.SlowGlow or FlashPatternId.DanceToMusic)
        {
            sampledWaves[0] = (float)NewPatterns.SlowGlow(pattern == FlashPatternId.SlowGlow ? segmentStart : 0).Evaluate(position);
            return LightSource.SlowGlow;
        }

        if (pattern == FlashPatternId.Waves)
        {
            for (int k = 0; k < sampledWaves.Length; k++)
            {
                sampledWaves[k] = (float)NewPatterns.Waves(k, segmentStart).Evaluate(position);
            }

            return LightSource.Waves;
        }

        return LightSource.Steps;
    }

    private enum LightSource
    {
        Steps,
        SlowGlow,
        Waves,
        Music,
    }
}
