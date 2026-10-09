namespace HolidayLights.Rendering;

/// <summary>Construction options of the presenter.</summary>
public sealed record LightsPresenterOptions
{
    /// <summary>Use WARP from the start (tests, machines without a hardware device). Normally false: WARP is used only after two device losses within a minute.</summary>
    public bool ForceSoftwareRendering { get; init; }
}
