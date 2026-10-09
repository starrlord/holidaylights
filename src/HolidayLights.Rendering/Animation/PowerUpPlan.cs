namespace HolidayLights.Rendering.Animation;

/// <summary>
/// The timeline of the power-up wave (PRODUCT-SPEC 4.3.2): every bulb appears dark and fades in over 200 ms; from 300 ms a
/// wave runs clockwise around each ring and bulb <c>k</c> of a ring of <c>R</c> bulbs reaches full brightness at
/// <c>300 + ring x k / R</c> ms (rising 120 ms, its glow 300 ms); the first-run variant then holds everything lit for 300 ms;
/// the pattern starts at step 0 at the end. Under reduced motion all bulbs fade in together over 300 ms.
/// </summary>
internal sealed class PowerUpPlan
{
    private readonly Dictionary<int, double> riseTimes;

    private PowerUpPlan(double sceneFadeIn, double patternStart, double waveStart, Dictionary<int, double> riseTimes, bool hasWave)
    {
        SceneFadeInMilliseconds = sceneFadeIn;
        PatternStartMilliseconds = patternStart;
        WaveStartMilliseconds = waveStart;
        this.riseTimes = riseTimes;
        HasWave = hasWave;
    }

    /// <summary>How long the dark bulbs take to fade in.</summary>
    public double SceneFadeInMilliseconds { get; }

    /// <summary>When the flash pattern starts at step 0, after the power-up began.</summary>
    public double PatternStartMilliseconds { get; }

    /// <summary>When the wave starts (bulbs without a ring position rise then).</summary>
    public double WaveStartMilliseconds { get; }

    /// <summary>False under reduced motion: the bulbs only fade in, in their pattern state.</summary>
    public bool HasWave { get; }

    /// <summary>True when <paramref name="transition"/> asks for a power-up (the theme transition ends with one).</summary>
    public static bool IsPowerUp(SceneTransition transition) =>
        transition is SceneTransition.FirstRunPowerUp or SceneTransition.AutostartPowerUp or SceneTransition.ShortPowerUp or SceneTransition.ThemeTransition;

    /// <summary>Plans the power-up of a layout.</summary>
    /// <param name="transition">A power-up transition (<see cref="IsPowerUp"/>).</param>
    /// <param name="reducedMotion">Windows animation effects are off.</param>
    /// <param name="layout">The layout whose rings the wave follows.</param>
    public static PowerUpPlan Create(SceneTransition transition, bool reducedMotion, LightsLayout layout)
    {
        if (reducedMotion)
        {
            return new PowerUpPlan(MotionTimings.ReducedMotionPowerUp, 0, 0, [], hasWave: false);
        }

        double ring = transition switch
        {
            SceneTransition.FirstRunPowerUp => MotionTimings.FirstRunRing,
            SceneTransition.AutostartPowerUp => MotionTimings.AutostartRing,
            _ => MotionTimings.ShortRing,
        };
        double hold = transition == SceneTransition.FirstRunPowerUp ? MotionTimings.FirstRunHold : 0;
        double waveStart = MotionTimings.PowerUpWaveStart;

        var rise = new Dictionary<int, double>();
        foreach (LightsRing lightsRing in layout.Rings)
        {
            int count = lightsRing.Ordinals.Count;
            for (int k = 0; k < count; k++)
            {
                rise[lightsRing.Ordinals[k]] = waveStart + ring * k / count;
            }
        }

        return new PowerUpPlan(MotionTimings.PowerUpDarkFadeIn, waveStart + ring + hold, waveStart, rise, hasWave: true);
    }

    /// <summary>When a bulb reaches full brightness (ms after the power-up began).</summary>
    public double FullBrightnessAt(int ordinal) => riseTimes.TryGetValue(ordinal, out double time) ? time : WaveStartMilliseconds;
}
