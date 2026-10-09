namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// The moving parts of one display's saver, advanced in fixed 60 ms steps (5.4 <c>Bulbs_OnTimer</c> order: the module,
/// then the message). Sprites advance a frame every Flash Interval steps, also with "Don't Flash" (PRODUCT-SPEC 6.2.3).
/// </summary>
/// <remarks>Pure and deterministic for a given random stream; not thread-safe.</remarks>
internal sealed class SaverSimulation
{
    private readonly SaverStepContext context;
    private readonly int frameInterval;

    /// <summary>Creates the simulation.</summary>
    /// <param name="module">The animation, or null for "(None)".</param>
    /// <param name="message">The bouncing message, or null when there is none on this display.</param>
    /// <param name="frameInterval">Steps per sprite frame (the Flash Interval, 1-9).</param>
    /// <param name="bulbs">Where snow sticks, or null.</param>
    public SaverSimulation(SaverModule? module, TextBounce? message, int frameInterval, ISnowCatcher? bulbs)
    {
        Module = module;
        Message = message;
        this.frameInterval = Math.Clamp(frameInterval, FlashSettings.MinInterval, FlashSettings.MaxInterval);
        context = new SaverStepContext(bulbs);
    }

    /// <summary>The animation, or null.</summary>
    public SaverModule? Module { get; }

    /// <summary>The message, or null.</summary>
    public TextBounce? Message { get; }

    /// <summary>Steps done so far.</summary>
    public long StepCount { get; private set; }

    /// <summary>The sprites the last step drew, in drawing order.</summary>
    public IReadOnlyList<SpriteDraw> Sprites => context.Sprites;

    /// <summary>The stamps the last step made (renderers add them to their background for good).</summary>
    public IReadOnlyList<SpriteStamp> NewStamps => context.Stamps;

    /// <summary>The snow that stuck to bulbs in the last step (renderers keep it for good).</summary>
    public IReadOnlyList<SnowCell> NewSnowCells => context.SnowCells;

    /// <summary>Advances one 60 ms step.</summary>
    public void Step()
    {
        context.Clear();
        StepCount++;
        context.FrameStep = StepCount % frameInterval == 0;
        Module?.Step(context);
        Message?.Step();
    }
}
