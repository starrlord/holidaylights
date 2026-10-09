namespace HolidayLights.Rendering.Animation;

/// <summary>The lights' part of the motion catalogue (PRODUCT-SPEC 4.3.2, 4.3.3, 4.4, 3.12), in milliseconds.</summary>
internal static class MotionTimings
{
    /// <summary>Arrangement edits: new bulbs fade in and removed bulbs fade out over 150 ms.</summary>
    public const double BulbFade = 150;

    /// <summary>Layer move (Bulb Drawing change, hot key): fade out 150 ms, move, fade in 150 ms.</summary>
    public const double LayerMoveFade = 150;

    /// <summary>Lights off and exit: the bulbs fade out over 300 ms, then the layers hide.</summary>
    public const double LightsOffFade = 300;

    /// <summary>Resting and resuming: fade out / in over 300 ms.</summary>
    public const double RestFade = 300;

    /// <summary>Theme transition: the old lights fade out over 250 ms before the short power-up.</summary>
    public const double ThemeFadeOut = 250;

    /// <summary>Theme transition under reduced motion: a 300 ms crossfade.</summary>
    public const double ReducedMotionCrossfade = 300;

    /// <summary>Power-up under reduced motion: every bulb fades in together over 300 ms.</summary>
    public const double ReducedMotionPowerUp = 300;

    /// <summary>Power-up: the dark bulbs fade in over 200 ms.</summary>
    public const double PowerUpDarkFadeIn = 200;

    /// <summary>Power-up: the wave starts 300 ms after the power-up.</summary>
    public const double PowerUpWaveStart = 300;

    /// <summary>Power-up: a bulb rises to full brightness over 120 ms (ease-out).</summary>
    public const double PowerUpRise = 120;

    /// <summary>Power-up: a bulb's glow rises over 300 ms.</summary>
    public const double PowerUpGlowRise = 300;

    /// <summary>Power-up: other bulbs start at 40 % opacity.</summary>
    public const double PowerUpDimOpacity = 0.4;

    /// <summary>First-run power-up: the wave takes 1600 ms around a ring.</summary>
    public const double FirstRunRing = 1600;

    /// <summary>First-run power-up: every light bulb stays lit with full glow for 300 ms before the pattern starts.</summary>
    public const double FirstRunHold = 300;

    /// <summary>Sign-in autostart power-up ring (no hold).</summary>
    public const double AutostartRing = 1000;

    /// <summary>"Show Lights" and theme transition power-up ring (no hold).</summary>
    public const double ShortRing = 800;

    /// <summary>On-screen pill and Identify fades.</summary>
    public const double OverlayFade = 150;

    /// <summary>On-screen pill hold time.</summary>
    public const double PillHold = 1600;
}
