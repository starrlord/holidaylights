namespace HolidayLights.Core.Abstractions;

/// <summary>What one bulb shows at a moment (PRODUCT-SPEC 5.8).</summary>
/// <param name="Frame">The frame to show, already reduced modulo the animation's frame count. For light bulbs it is the lit or unlit frame of the current step.</param>
/// <param name="Brightness">Light bulbs: opacity of the lit visual drawn over the unlit visual (0-1). Animation and static bulbs: 1.</param>
/// <param name="Glow">Opacity multiplier of the glow visual before the global intensity (0-1). Light bulbs: equal to <paramref name="Brightness"/> except during the power-up wave; other bulbs: 0.</param>
public record struct BulbVisualState(int Frame, float Brightness, float Glow);

/// <summary>Inputs of a flash sequencer that do not change while it runs.</summary>
public sealed record FlashOptions
{
    /// <summary>The pattern (the effective one: energy-saver and limit rules already applied by the scene builder).</summary>
    public FlashPatternId Pattern { get; init; } = FlashPatternId.FlashTogether;

    /// <summary>Light-bulb changes in discrete patterns are fades rather than switches (PRODUCT-SPEC 5.8).</summary>
    public bool SmoothFading { get; init; } = true;

    /// <summary>"Limit Flashing to 3 Flashes per Second" (5.5.3): Twinkle, Chase Around and Dance retrigger a bulb at most every 333 ms.</summary>
    public bool LimitFlashing { get; init; }

    /// <summary>Energy saver "Stop Flashing" (5.12.3): every bulb shows frame 0 (lit); no clock is needed.</summary>
    public bool StopFlashing { get; init; }

    /// <summary>
    /// Seed of <c>u(i, s)</c> = SplitMix64(ringSeed XOR (ring index &lt;&lt; 32) XOR s), where ringSeed =
    /// <c>DisplaySeeds.PatternSeed(PatternSeed, ring id)</c> (<see cref="PatternSeed"/> itself on ring 0; PO decision 5); taken
    /// from the clock when the pattern starts, fixed in tests.
    /// </summary>
    public ulong PatternSeed { get; init; }

    /// <summary>The MSVC <c>srand</c> seed for Random Flashing (5.4 used the millisecond of the system time; golden data uses 1).</summary>
    public uint ClassicRandomSeed { get; init; } = 1;
}

/// <summary>Per-bulb facts the sequencer derived from the layout and the bulbs.</summary>
/// <param name="Ordinal">The placement ordinal.</param>
/// <param name="Kind">Static, light bulb or animation (of this placement's slot and flavor).</param>
/// <param name="FrameCount">Frames of the animation.</param>
/// <param name="LitFrame">The lit frame of a light bulb (0 or 1); 0 otherwise.</param>
/// <param name="StripFrameCount">The 5.4 frame count of the bulb's strip (Don't Flash 1, Random 8, else the largest phase count; corners count on Top/Bottom).</param>
/// <param name="MusicGroup">Dance group: <c>q mod 12</c> for side light bulbs, <see cref="DanceEnvelope.CornerGroup"/> for corner light bulbs, -1 for other bulbs.</param>
/// <param name="LightIndex">q: index among light bulbs in ring order, or -1.</param>
/// <param name="AnimationIndex">a: index among animation bulbs in ring order, or -1.</param>
public sealed record FlashBulbInfo(int Ordinal, BulbAnimationKind Kind, int FrameCount, int LitFrame, int StripFrameCount, int MusicGroup, int LightIndex, int AnimationIndex);

/// <summary>Durations of light-bulb fades in discrete patterns (PRODUCT-SPEC 5.8): turning on ramps 0 to 1, turning off ramps 1 to 0, linearly.</summary>
/// <param name="TurnOnMilliseconds">Duration of a 0-to-1 change (0 = instant).</param>
/// <param name="TurnOffMilliseconds">Duration of a 1-to-0 change (0 = instant).</param>
public readonly record struct FadeProfile(double TurnOnMilliseconds, double TurnOffMilliseconds)
{
    /// <summary>No fading.</summary>
    public static FadeProfile Instant => default;
}

/// <summary>
/// A continuous raised-cosine brightness: <c>b(x) = 0.5 - 0.5 cos(2 pi (x / PeriodSteps - PhaseOffset))</c> where
/// <c>x</c> is the step position (<see cref="StepClock.PositionAt"/>). Slow Glow: period 16, offset 0. Waves: offset q / 16.
/// </summary>
/// <param name="PeriodSteps">Period in steps (16 for Slow Glow and Waves).</param>
/// <param name="PhaseOffset">Offset in periods (0-1).</param>
public readonly record struct BrightnessWave(double PeriodSteps, double PhaseOffset)
{
    /// <summary>Evaluates the wave.</summary>
    /// <param name="stepPosition">The step position x.</param>
    /// <returns>Brightness 0-1.</returns>
    public double Evaluate(double stepPosition) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * (stepPosition / PeriodSteps - PhaseOffset));
}

/// <summary>Timing of "Dance to the Music" brightness (PRODUCT-SPEC 5.10), shared by the sequencer and the renderer.</summary>
public static class DanceEnvelope
{
    /// <summary>Attack: the brightness rises to the new peak over 30 ms.</summary>
    public const double AttackMilliseconds = 30;

    /// <summary>Decay time constant: <c>B = B x exp(-dt / 250 ms)</c>.</summary>
    public const double DecayTauMilliseconds = 250;

    /// <summary>Music events are coalesced in batches of 30 ms.</summary>
    public const double BatchMilliseconds = 30;

    /// <summary>The group of corner light bulbs ("group C").</summary>
    public const int CornerGroup = 12;

    /// <summary>Number of groups: 12 pitch classes plus the corners.</summary>
    public const int GroupCount = 13;

    /// <summary>Brightness of a group after an event: linear attack from <paramref name="start"/> to <paramref name="peak"/>, then exponential decay.</summary>
    /// <param name="start">Brightness when the event arrived.</param>
    /// <param name="peak">Brightness reached at the end of the attack.</param>
    /// <param name="millisecondsSinceEvent">Time since the event (the moment the sound is heard).</param>
    /// <returns>Brightness 0-1.</returns>
    public static double BrightnessAt(double start, double peak, double millisecondsSinceEvent)
    {
        if (millisecondsSinceEvent <= 0)
        {
            return start;
        }

        if (millisecondsSinceEvent < AttackMilliseconds)
        {
            return start + (peak - start) * (millisecondsSinceEvent / AttackMilliseconds);
        }

        return peak * Math.Exp(-(millisecondsSinceEvent - AttackMilliseconds) / DecayTauMilliseconds);
    }
}

/// <summary>A new brightness envelope for one Dance group, created by a batch of music events.</summary>
/// <param name="Group">The group (0-11 pitch classes, <see cref="DanceEnvelope.CornerGroup"/>).</param>
/// <param name="StartBrightness">The group's brightness when the event arrived.</param>
/// <param name="PeakBrightness">The brightness after the attack.</param>
/// <param name="Timestamp">When the sound is heard (<see cref="MusicEvent.Timestamp"/>).</param>
public readonly record struct DanceGroupUpdate(int Group, float StartBrightness, float PeakBrightness, long Timestamp);

/// <summary>What a batch of music events changed (Dance to the Music only).</summary>
/// <param name="Groups">Groups that got a new envelope.</param>
/// <param name="FramesChanged">Animation bulbs advanced a frame on a beat; re-read <see cref="IFlashSequencer.Current"/>.</param>
/// <param name="ModeChanged">Dance switched between following the music and the idle Slow Glow; re-read <see cref="IFlashSequencer.GetWave"/> for every light bulb.</param>
public sealed record MusicResponse(IReadOnlyList<DanceGroupUpdate> Groups, bool FramesChanged, bool ModeChanged)
{
    /// <summary>Nothing changed.</summary>
    public static MusicResponse None { get; } = new([], false, false);
}

/// <summary>
/// The state machine of one flash pattern over one <see cref="LightsLayout"/> (PRODUCT-SPEC 5.6-5.10). Created by
/// <see cref="IFlashEngine"/>; owned and used by one thread at a time (not thread-safe).
/// </summary>
/// <remarks>
/// <para>Discrete evaluation: <see cref="MoveTo"/> the step and read <see cref="Current"/> (target states, no fades). The
/// renderer turns brightness changes into opacity animations with <see cref="GetFadeProfile"/>, continuous brightness into
/// repeating animations with <see cref="GetWave"/>, and music into envelopes with <see cref="ApplyMusicEvents"/>.</para>
/// <para>CPU previews call <see cref="Sample"/>, which resolves fades, waves and Dance envelopes at a timestamp, so a preview
/// and the desktop show the same picture for the same layout, options and clock.</para>
/// <para>Stateful patterns (Twinkle, Dance) replay from step 0 when moved backwards, so results depend only on the
/// options, the layout and the step.</para>
/// <para>Classic patterns (Flash Together, Alternating, Bulb Chase, Random Flashing; not Stop Flashing): a light bulb's
/// <see cref="Current"/> state at step s equals its state at s + <see cref="FlashBulbInfo.StripFrameCount"/>, and its Glow
/// equals its Brightness. The renderer binds one repeating animation per light bulb on that basis; it checks two periods
/// and falls back to per-step updates when they do not repeat.</para>
/// </remarks>
public interface IFlashSequencer
{
    /// <summary>The layout the sequencer was built for.</summary>
    LightsLayout Layout { get; }

    /// <summary>The options it was built with.</summary>
    FlashOptions Options { get; }

    /// <summary>The step of <see cref="Current"/>.</summary>
    long Step { get; }

    /// <summary>Target states at <see cref="Step"/>, indexed by placement ordinal.</summary>
    ReadOnlySpan<BulbVisualState> Current { get; }

    /// <summary>True while "Dance to the Music" follows music (false: idle Slow Glow, or another pattern).</summary>
    bool IsDancing { get; }

    /// <summary>Returns the per-bulb facts.</summary>
    /// <param name="ordinal">A placement ordinal.</param>
    /// <returns>The facts.</returns>
    FlashBulbInfo GetBulb(int ordinal);

    /// <summary>Evaluates a step (advancing stateful patterns one step at a time; moving backwards replays from 0).</summary>
    /// <param name="step">The step.</param>
    /// <param name="timestamp">When the step begins (Dance uses it to detect 2 s without a beat).</param>
    void MoveTo(long step, long timestamp);

    /// <summary>Fade durations for discrete patterns at a step period (classic: d = min(0.4 P, 150 ms); Twinkle: d = 0.8 P; Chase Around: d = 0.5 P; on = 0.6 d, off = d). Instant when fading is off.</summary>
    /// <param name="stepPeriodMilliseconds">P.</param>
    /// <returns>The fade durations.</returns>
    FadeProfile GetFadeProfile(double stepPeriodMilliseconds);

    /// <summary>The continuous brightness of a light bulb (Slow Glow, Waves, idle Dance), or null when its brightness is discrete or event-driven.</summary>
    /// <param name="ordinal">A placement ordinal.</param>
    /// <returns>The wave, or null.</returns>
    BrightnessWave? GetWave(int ordinal);

    /// <summary>Feeds music events (Dance only; other patterns ignore them and return <see cref="MusicResponse.None"/>).</summary>
    /// <param name="events">Events in timestamp order (a 30 ms batch).</param>
    /// <returns>What changed.</returns>
    MusicResponse ApplyMusicEvents(ReadOnlySpan<MusicEvent> events);

    /// <summary>Resolves the complete visual state at a moment, including fades, waves and Dance envelopes (for CPU previews).</summary>
    /// <param name="timestamp">The moment (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>).</param>
    /// <param name="clock">The clock that maps the timestamp to a step position.</param>
    /// <param name="destination">One state per placement ordinal.</param>
    void Sample(long timestamp, StepClock clock, Span<BulbVisualState> destination);
}

/// <summary>Creates flash sequencers. Implemented by core-layout; pure and thread-safe.</summary>
public interface IFlashEngine
{
    /// <summary>Builds the per-bulb model of a pattern for a layout (strip frame counts, chase indices, Random Flashing tables, ring indices, Dance groups).</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="bulbs">Resolves the bulbs of the layout (phase counts, animation kinds).</param>
    /// <param name="options">Pattern and seeds.</param>
    /// <returns>A new sequencer at step 0.</returns>
    IFlashSequencer CreateSequencer(LightsLayout layout, IBulbResolver bulbs, FlashOptions options);
}
