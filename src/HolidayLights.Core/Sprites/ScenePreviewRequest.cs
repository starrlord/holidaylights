namespace HolidayLights.Core.Sprites;

/// <summary>What a <see cref="ScenePreviewRenderer"/> paints behind the lights.</summary>
public enum PreviewBackdrop
{
    /// <summary>Nothing: new images stay transparent, existing pixels (e.g. a wallpaper drawn by the caller) are kept.</summary>
    None,

    /// <summary>The night gradient (#0B1530 at the top to #1D3466 at the bottom of each display; PRODUCT-SPEC 4.2).</summary>
    NightGradient,

    /// <summary>A solid colour (<see cref="ScenePreviewRequest.BackdropColor"/>), e.g. the desktop background colour.</summary>
    SolidColor,
}

/// <summary>What to render of a <see cref="LightsScene"/> and how.</summary>
public sealed record ScenePreviewRequest
{
    /// <summary>The scene: displays (bounds, work areas), layout, look.</summary>
    public required LightsScene Scene { get; init; }

    /// <summary>Resolves the bulbs of the layout.</summary>
    public required IBulbResolver Bulbs { get; init; }

    /// <summary>
    /// One state per placement ordinal (from <see cref="IFlashSequencer.Sample"/>), or null for the static picture: frame 0
    /// of every bulb, light bulbs lit (<see cref="ScenePreviewRenderer.CreateStaticStates"/>).
    /// </summary>
    public IReadOnlyList<BulbVisualState>? States { get; init; }

    /// <summary>Output pixels per physical display pixel (1 for full size; sprites are requested at S x Zoom).</summary>
    public double Zoom { get; init; } = 1.0;

    /// <summary>What lies behind the lights.</summary>
    public PreviewBackdrop Backdrop { get; init; } = PreviewBackdrop.NightGradient;

    /// <summary>The colour of <see cref="PreviewBackdrop.SolidColor"/>.</summary>
    public RgbColor BackdropColor { get; init; } = RgbColor.Black;

    /// <summary>Draws the taskbar band (#202020 at 85 %) over the part of each display outside its work area.</summary>
    public bool DrawTaskbarBand { get; init; } = true;

    /// <summary>The pixel style, or null for the scene's (<see cref="SceneEffects.Pixels"/>).</summary>
    public SpriteStyle? Style { get; init; }

    /// <summary>
    /// The glow intensity, or null for the scene's (<see cref="GlowLevels.Intensity"/> of <see cref="SceneEffects.Glow"/>).
    /// 0 draws no glow (High Contrast, light backgrounds).
    /// </summary>
    public float? GlowIntensity { get; init; }
}
