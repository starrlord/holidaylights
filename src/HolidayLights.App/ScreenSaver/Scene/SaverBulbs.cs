namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>
/// The bulbs around one display of the saver (PRODUCT-SPEC 6.2.2): the current arrangement laid out around the whole
/// display (the saver covers the taskbar, as 5.4), with its own step clock and the current pattern, Look and glow.
/// </summary>
/// <remarks>Used by one thread at a time (the flash sequencer is not thread-safe).</remarks>
internal sealed class SaverBulbs
{
    private readonly IFlashSequencer sequencer;
    private readonly BulbVisualState[] states;

    private SaverBulbs(LightsLayout layout, IFlashSequencer sequencer, StepClock clock, double scale)
    {
        Layout = layout;
        this.sequencer = sequencer;
        Clock = clock;
        Scale = scale;
        states = new BulbVisualState[layout.Placements.Count];
    }

    /// <summary>The layout (physical pixels, virtual-screen coordinates).</summary>
    public LightsLayout Layout { get; }

    /// <summary>The bulbs' step clock (started with the saver).</summary>
    public StepClock Clock { get; }

    /// <summary>S: art pixels to physical pixels (display scale x bulb size).</summary>
    public double Scale { get; }

    /// <summary>The states of the last <see cref="Sample"/>, by placement ordinal (a live view: it changes with every sample).</summary>
    public IReadOnlyList<BulbVisualState> States => states;

    /// <summary>Lays out the arrangement around a display and starts its pattern at step 0.</summary>
    /// <param name="display">The display.</param>
    /// <param name="options">The saver options.</param>
    /// <param name="bulbs">Resolves bulbs.</param>
    /// <param name="layoutEngine">The layout engine.</param>
    /// <param name="flashEngine">The flash engine.</param>
    /// <param name="startTimestamp">When step 0 begins (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>).</param>
    /// <returns>The bulbs.</returns>
    public static SaverBulbs Create(
        DisplayInfo display, SaverOptions options, IBulbResolver bulbs, ILayoutEngine layoutEngine, IFlashEngine flashEngine, long startTimestamp)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(flashEngine);
        double scale = ArtScale.Effective(display.Scale, options.BulbSize);
        LightsLayout layout = layoutEngine.Layout(
            [new LayoutTarget(display.DeviceId, display.Bounds, scale)], options.Arrangement, bulbs, FrameMode.EachDisplay);
        IFlashSequencer sequencer = flashEngine.CreateSequencer(layout, bulbs, options.Flash);
        var saverBulbs = new SaverBulbs(layout, sequencer, StepClock.Start(startTimestamp, options.Interval), scale);
        saverBulbs.Sample(startTimestamp);
        return saverBulbs;
    }

    /// <summary>Resolves every bulb's state at a moment (fades, waves and Dance envelopes included).</summary>
    /// <param name="timestamp">The moment.</param>
    public void Sample(long timestamp) => sequencer.Sample(timestamp, Clock, states);

    /// <summary>Feeds music events to "Dance to the Music" (other patterns ignore them).</summary>
    /// <param name="events">Events in timestamp order.</param>
    public void ApplyMusic(ReadOnlySpan<MusicEvent> events) => sequencer.ApplyMusicEvents(events);
}
