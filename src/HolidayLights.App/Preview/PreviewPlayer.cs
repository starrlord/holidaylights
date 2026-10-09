using System.Diagnostics;
using HolidayLights.Core.Flash;

namespace HolidayLights.App.Preview;

/// <summary>
/// Plays a flash pattern over a preview layout exactly like the desktop (PRODUCT-SPEC 3.0.2, 5.5.2): the same flash
/// engine, brightness model and step clock, sampled at a timestamp; knows when the next redraw is due (once per step,
/// at most 30 times per second while fades or continuous patterns run). "Dance to the Music" listens to the music.
/// </summary>
/// <remarks>Owned by one thread (the UI thread of the preview that created it).</remarks>
public sealed class PreviewPlayer : IDisposable
{
    /// <summary>The shortest time between redraws while light changes (30 per second).</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30);

    private readonly IFlashSequencer sequencer;
    private readonly IMusicEventReader? music;
    private readonly MusicEvent[] musicBuffer;
    private readonly bool continuous;

    /// <summary>Creates a player.</summary>
    /// <param name="engine">The flash engine.</param>
    /// <param name="bulbs">Resolves the bulbs of the layout.</param>
    /// <param name="layout">The preview layout.</param>
    /// <param name="options">The pattern and seeds (the desktop's effective options, or a theme's own).</param>
    /// <param name="musicEvents">The music events ("Dance to the Music"), or null.</param>
    public PreviewPlayer(IFlashEngine engine, IBulbResolver bulbs, LightsLayout layout, FlashOptions options, IMusicEventSource? musicEvents)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        Layout = layout;
        Options = options;
        sequencer = engine.CreateSequencer(layout, bulbs, options);
        States = new BulbVisualState[layout.Placements.Count];
        continuous = options.Pattern is FlashPatternId.SlowGlow or FlashPatternId.Waves or FlashPatternId.DanceToMusic;
        if (options.Pattern == FlashPatternId.DanceToMusic && musicEvents is not null)
        {
            music = musicEvents.Subscribe(256);
            musicBuffer = new MusicEvent[256];
        }
        else
        {
            musicBuffer = [];
        }
    }

    /// <summary>The layout played.</summary>
    public LightsLayout Layout { get; }

    /// <summary>The options played.</summary>
    public FlashOptions Options { get; }

    /// <summary>The states of the last <see cref="Sample"/>, one per placement ordinal.</summary>
    public BulbVisualState[] States { get; }

    /// <summary>True when nothing ever changes (Don't Flash, Stop Flashing).</summary>
    public bool IsStatic => !FlashClock.RunsClock(Options);

    /// <summary>Computes <see cref="States"/> at a moment.</summary>
    /// <param name="timestamp">A <see cref="Stopwatch.GetTimestamp"/> value.</param>
    /// <param name="clock">The step clock (the desktop's, or the preview's own).</param>
    /// <returns><see cref="States"/>.</returns>
    public BulbVisualState[] Sample(long timestamp, StepClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (music is not null)
        {
            int count;
            while ((count = music.Read(musicBuffer)) > 0)
            {
                sequencer.ApplyMusicEvents(musicBuffer.AsSpan(0, count));
            }
        }

        sequencer.Sample(timestamp, clock, States);
        return States;
    }

    /// <summary>When the picture changes next: the next step boundary, or sooner while a fade or a continuous pattern runs.</summary>
    /// <param name="timestamp">Now.</param>
    /// <param name="clock">The step clock.</param>
    /// <returns>A timestamp, or <see cref="long.MaxValue"/> for a static pattern.</returns>
    public long NextRedraw(long timestamp, StepClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (IsStatic)
        {
            return long.MaxValue;
        }

        long frame = timestamp + Ticks(FrameInterval.TotalMilliseconds);
        if (continuous)
        {
            return frame;
        }

        long step = clock.StepAt(timestamp);
        long boundary = clock.TimestampOfStep(step + 1);
        FadeProfile fade = sequencer.GetFadeProfile(clock.StepPeriodMilliseconds);
        double fadeMs = Math.Max(fade.TurnOnMilliseconds, fade.TurnOffMilliseconds);
        long fadeEnd = clock.TimestampOfStep(step) + Ticks(fadeMs);
        return fadeMs > 0 && timestamp < fadeEnd ? Math.Min(frame, boundary) : boundary;
    }

    /// <summary>Stops listening to the music.</summary>
    public void Dispose() => music?.Dispose();

    private static long Ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000.0);
}
