namespace HolidayLights.Rendering.Engine;

/// <summary>What the engine reports to the presenter (called on the Lights thread).</summary>
internal interface IEngineHost
{
    /// <summary>How often the Lights thread was restarted after a crash.</summary>
    int RestartCount { get; }

    /// <summary>The status changed.</summary>
    void PublishStatus(LightsStatus status);

    /// <summary>The step clock was restarted or its speed changed.</summary>
    void PublishClock(StepClock clock);

    /// <summary>New diagnostics are available.</summary>
    void PublishDiagnostics(LightsDiagnostics diagnostics);
}

/// <summary>The services the engine works with.</summary>
/// <param name="Bulbs">Resolves bulbs.</param>
/// <param name="Sprites">Scaled sprites and glow halos.</param>
/// <param name="Flash">Flash sequencers.</param>
/// <param name="Music">Music events for Dance to the Music.</param>
/// <param name="Options">Presenter options.</param>
/// <param name="Log">The log.</param>
internal sealed record EngineDependencies(
    IBulbResolver Bulbs, ISpriteProvider Sprites, IFlashEngine Flash, IMusicEventSource Music, LightsPresenterOptions Options, IAppLog Log);

/// <summary>A requested scene and how to get there.</summary>
/// <param name="Scene">The scene.</param>
/// <param name="Transition">The transition.</param>
internal readonly record struct SceneRequest(LightsScene Scene, SceneTransition Transition)
{
    /// <summary>
    /// Combines a request that has not been shown yet with a newer one: the newer scene wins, but an animated transition
    /// that was asked for (a power-up, the theme transition) is not lost to a following ordinary edit.
    /// </summary>
    public static SceneRequest Merge(SceneRequest? pending, SceneRequest next) =>
        pending is { } earlier && next.Transition == SceneTransition.Automatic && earlier.Transition != SceneTransition.Automatic
            ? next with { Transition = earlier.Transition }
            : next;
}
