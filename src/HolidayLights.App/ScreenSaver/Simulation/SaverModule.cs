namespace HolidayLights.App.ScreenSaver.Simulation;

/// <summary>
/// One screen saver animation "module" (5.4 <c>ScreenSaverModule</c>): Snow, Snow Flakes, Balloons or the floaters of the
/// other animations. Each 60 ms step moves everything once with the 5.4 per-step constants and records what it drew.
/// </summary>
/// <remarks>Not thread-safe: one thread steps a module.</remarks>
internal abstract class SaverModule
{
    /// <summary>Creates the module.</summary>
    /// <param name="field">The simulated screen.</param>
    /// <param name="random">The random stream (5.4 <c>rand()</c>).</param>
    protected SaverModule(SaverField field, ISaverRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Field = field;
        Random = random;
    }

    /// <summary>
    /// How far above the bottom edge the module can stamp sprites into the background, in DIPs (0 when it never stamps).
    /// Renderers keep a stamp layer of this height.
    /// </summary>
    public abstract int StampReach { get; }

    /// <summary>The simulated screen.</summary>
    protected SaverField Field { get; }

    /// <summary>The random stream.</summary>
    protected ISaverRandom Random { get; }

    /// <summary>Moves everything one step and records the sprites drawn, the stamps and the snow that stuck.</summary>
    /// <param name="context">The step's frame-step flag, snow catcher and output lists (already cleared).</param>
    public abstract void Step(SaverStepContext context);
}
