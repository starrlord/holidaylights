using HolidayLights.Core.Flash;

namespace HolidayLights.App.Shell;

/// <summary>
/// The rules that turn settings, displays and the Energy Saver state into the effective lights (PRODUCT-SPEC 5.2.1,
/// 5.2.2, 5.5.3, 5.8, 5.9, 5.12.1, 5.12.3, 4.5). Pure functions.
/// </summary>
public static class SceneRules
{
    /// <summary>The smallest interval while "Use Less Power" applies (300 ms per step).</summary>
    public const int LessPowerMinimumInterval = 5;

    /// <summary>
    /// Every connected display with its "Show Lights" choice (<c>lights.displays.disabled</c> by device id). At least one
    /// display stays on: when every connected display is disabled, the main display shows lights.
    /// </summary>
    /// <param name="displays">The connected displays (main display first).</param>
    /// <param name="disabled">Device ids of displays without lights.</param>
    /// <returns>One entry per display, in the order given.</returns>
    public static IReadOnlyList<DisplayScene> SelectDisplays(IReadOnlyList<DisplayInfo> displays, IReadOnlyList<string> disabled)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(disabled);
        var off = new HashSet<string>(disabled, StringComparer.OrdinalIgnoreCase);
        DisplayScene[] scenes = [.. displays.Select(d => new DisplayScene(d, !off.Contains(d.DeviceId)))];
        if (scenes.Length > 0 && !scenes.Any(s => s.Enabled))
        {
            int main = Array.FindIndex(scenes, s => s.Display.IsPrimary);
            int index = main >= 0 ? main : 0;
            scenes[index] = scenes[index] with { Enabled = true };
        }

        return scenes;
    }

    /// <summary>The areas to frame: each enabled display's work area at <c>S = display scale x Bulb Size</c>.</summary>
    /// <param name="displays">The displays of the scene.</param>
    /// <param name="size">"Bulb Size".</param>
    /// <returns>One target per enabled display, in display order.</returns>
    public static IReadOnlyList<LayoutTarget> Targets(IEnumerable<DisplayScene> displays, BulbSize size)
    {
        ArgumentNullException.ThrowIfNull(displays);
        return [.. displays
            .Where(d => d.Enabled)
            .Select(d => new LayoutTarget(d.Display.DeviceId, d.Display.WorkArea, ArtScale.Effective(d.Display.Scale, size)))];
    }

    /// <summary>The "When Energy Saver Is On" rule in effect, or null when Energy Saver is off or the choice is "Change Nothing".</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="energySaverOn">Windows Energy Saver is on.</param>
    /// <returns>The rule that shapes the scene, or null.</returns>
    public static EnergySaverChoice? EnergySaverInEffect(AppSettings settings, bool energySaverOn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnergySaverChoice choice = settings.Rest.EnergySaver;
        return energySaverOn && choice != EnergySaverChoice.ChangeNothing ? choice : null;
    }

    /// <summary>"Show Lights", unless "Turn Off the Lights" applies while Energy Saver is on.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="energySaverOn">Windows Energy Saver is on.</param>
    /// <returns>True when the lights show.</returns>
    public static bool LightsOn(AppSettings settings, bool energySaverOn) =>
        settings.Lights.On && EnergySaverInEffect(settings, energySaverOn) != EnergySaverChoice.TurnOffLights;

    /// <summary>
    /// The effective flash interval: the chosen one, at least 3 with "Limit Flashing to 3 Flashes per Second", at least 5
    /// under "Use Less Power".
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="energySaverOn">Windows Energy Saver is on.</param>
    /// <returns>The interval (1-9).</returns>
    public static int EffectiveInterval(AppSettings settings, bool energySaverOn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        int interval = FlashClock.LimitInterval(settings.Current.Flash.Interval, settings.Accessibility.LimitFlashing);
        return EnergySaverInEffect(settings, energySaverOn) == EnergySaverChoice.UseLessPower
            ? Math.Max(interval, LessPowerMinimumInterval)
            : interval;
    }

    /// <summary>
    /// The effective flash options without seeds: Smooth Fading forced on by the flash limit, off under "Use Less Power"
    /// and "Stop Flashing" (which also freezes every bulb on frame 0).
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="energySaverOn">Windows Energy Saver is on.</param>
    /// <returns>The options; the caller adds the seeds.</returns>
    public static FlashOptions FlashOptions(AppSettings settings, bool energySaverOn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnergySaverChoice? rule = EnergySaverInEffect(settings, energySaverOn);
        bool savingPower = rule is EnergySaverChoice.UseLessPower or EnergySaverChoice.StopFlashing;
        bool limit = settings.Accessibility.LimitFlashing;
        return new FlashOptions
        {
            Pattern = settings.Current.Flash.Pattern,
            SmoothFading = !savingPower && (settings.Look.SmoothFading || limit),
            LimitFlashing = limit,
            StopFlashing = rule == EnergySaverChoice.StopFlashing,
        };
    }

    /// <summary>The effective look: glow off under "Use Less Power" and "Stop Flashing"; reduced motion when Windows animation effects are off.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="energySaverOn">Windows Energy Saver is on.</param>
    /// <param name="animationsEnabled">Windows "Animation effects".</param>
    /// <returns>The effects.</returns>
    public static SceneEffects Effects(AppSettings settings, bool energySaverOn, bool animationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool savingPower = EnergySaverInEffect(settings, energySaverOn) is EnergySaverChoice.UseLessPower or EnergySaverChoice.StopFlashing;
        return new SceneEffects
        {
            Pixels = settings.Look.Pixels,
            Glow = savingPower ? GlowLevel.Off : settings.Look.Glow,
            ReducedMotion = !animationsEnabled,
        };
    }

    /// <summary>
    /// How the presenter should move to a new scene: the short power-up when the lights come on, the theme transition when
    /// a theme (or the 5.4 import, Reset) changed what the lights show, otherwise an automatic edit transition.
    /// </summary>
    /// <param name="change">Why the settings changed, or null when the change did not come from the settings.</param>
    /// <param name="previous">The previous scene.</param>
    /// <param name="next">The new scene.</param>
    /// <returns>The transition.</returns>
    public static SceneTransition ChooseTransition(SettingsChange? change, LightsScene previous, LightsScene next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        if (!previous.LightsOn && next.LightsOn)
        {
            return SceneTransition.ShortPowerUp;
        }

        bool themeLike = change?.Kind is SettingsChangeKind.ThemeLoaded or SettingsChangeKind.AutomaticTheme
            or SettingsChangeKind.Import or SettingsChangeKind.Reset;
        bool lookChanged = !ReferenceEquals(previous.Layout, next.Layout) || previous.Flash.Pattern != next.Flash.Pattern
            || previous.Interval != next.Interval;
        return themeLike && next.LightsOn && lookChanged ? SceneTransition.ThemeTransition : SceneTransition.Automatic;
    }
}
