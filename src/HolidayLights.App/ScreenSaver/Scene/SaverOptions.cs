using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Flash;

namespace HolidayLights.App.ScreenSaver.Scene;

/// <summary>
/// Everything one saver run follows, taken once from the settings when it starts (PRODUCT-SPEC 6.2, 3.5): the eight saver
/// theme values, "Show On", the arrangement and bulb size, and the effective pattern, speed and Look with the rules the
/// desktop applies too ("Limit Flashing": speed at least 3, fading on).
/// </summary>
internal sealed record SaverOptions
{
    /// <summary>Animation, style, message, font, colours, picture and placement.</summary>
    public required SaverLook Look { get; init; }

    /// <summary>"Show On".</summary>
    public required SaverDisplays ShowOn { get; init; }

    /// <summary>The bulbs around every display.</summary>
    public required SlotAssignment Arrangement { get; init; }

    /// <summary>"Bulb Size".</summary>
    public required BulbSize BulbSize { get; init; }

    /// <summary>The effective flash options.</summary>
    public required FlashOptions Flash { get; init; }

    /// <summary>The effective Flash Interval (1-9): the bulbs' step period and the sprites' frame interval.</summary>
    public required int Interval { get; init; }

    /// <summary>"Pixels" of the Look.</summary>
    public required SpriteStyle Pixels { get; init; }

    /// <summary>The glow intensity (0 = no glow).</summary>
    public required float GlowIntensity { get; init; }

    /// <summary>"Smooth Motion": interpolated sprites at the refresh rate; off = 16 steps per second (Classic 2003).</summary>
    public required bool SmoothMotion { get; init; }

    /// <summary>Takes the saver's options from the settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="seed">Seeds the new patterns and Random Flashing (a time-based value in a real run, fixed in tests).</param>
    /// <returns>The options.</returns>
    public static SaverOptions FromSettings(AppSettings settings, uint seed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool limit = settings.Accessibility.LimitFlashing;
        FlashSettings flash = settings.Current.Flash;
        return new SaverOptions
        {
            Look = settings.Current.Saver,
            ShowOn = settings.Saver.ShowOn,
            Arrangement = settings.Current.Arrangement,
            BulbSize = settings.Lights.Size,
            Flash = new FlashOptions
            {
                Pattern = flash.Pattern,
                SmoothFading = settings.Look.SmoothFading || limit,
                LimitFlashing = limit,
                PatternSeed = (ulong)seed << 32 | seed,
                ClassicRandomSeed = seed,
            },
            Interval = FlashClock.LimitInterval(flash.Interval, limit),
            Pixels = settings.Look.Pixels,
            GlowIntensity = GlowLevels.Intensity(settings.Look.Glow),
            SmoothMotion = settings.Look.SmoothSaverMotion,
        };
    }

    /// <summary>
    /// The options of one display of the run (PO decision 5): the main display (ordinal 0) keeps the run's seeds, so a single
    /// display twinkles and random-flashes exactly as before; every other display seeds Twinkle and Random Flashing with its
    /// own mix of the run's seeds and its ordinal (<see cref="HolidayLights.Core.Flash.DisplaySeeds"/>, the rule the desktop applies to its
    /// rings), so identical displays never flash in lockstep.
    /// </summary>
    /// <param name="ordinal">0 for the main display, then 1, 2, ... for the others in display order.</param>
    /// <returns>The options of that display.</returns>
    public SaverOptions ForDisplay(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (ordinal == 0)
        {
            return this;
        }

        // The one rule of core's DisplaySeeds, so the saver's Twinkle on display i equals the desktop's on ring i.
        return this with
        {
            Flash = Flash with
            {
                PatternSeed = HolidayLights.Core.Flash.DisplaySeeds.PatternSeed(Flash.PatternSeed, ordinal),
                ClassicRandomSeed = HolidayLights.Core.Flash.DisplaySeeds.ClassicRandomSeed(Flash.ClassicRandomSeed, ordinal),
            },
        };
    }
}

/// <summary>What one display shows in the saver.</summary>
/// <param name="Display">The display.</param>
/// <param name="ShowsContent">False for the other displays under "Main Display Only": they stay black (5.4).</param>
/// <param name="IsMain">True for the main display: the picture and the message appear only there.</param>
internal sealed record SaverDisplayPlan(DisplayInfo Display, bool ShowsContent, bool IsMain)
{
    /// <summary>The display in DIPs: the simulated screen (a 4K display at 150 % is 2560 x 1440).</summary>
    public SaverField Field => new(ToDips(Display.Bounds.Width), ToDips(Display.Bounds.Height));

    /// <summary>Physical pixels per DIP.</summary>
    public double Scale => Display.Scale;

    /// <summary>Plans every display: content on all of them, or on the main display only.</summary>
    /// <param name="displays">The displays, main display first.</param>
    /// <param name="showOn">"Show On".</param>
    /// <returns>One plan per display, in the same order.</returns>
    public static IReadOnlyList<SaverDisplayPlan> For(IReadOnlyList<DisplayInfo> displays, SaverDisplays showOn)
    {
        ArgumentNullException.ThrowIfNull(displays);
        DisplayInfo? main = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
        return [.. displays.Select(d =>
        {
            bool isMain = ReferenceEquals(d, main);
            return new SaverDisplayPlan(d, isMain || showOn == SaverDisplays.All, isMain);
        })];
    }

    private int ToDips(int pixels) => Math.Max(1, (int)Math.Round(pixels / Display.Scale, MidpointRounding.AwayFromZero));
}
