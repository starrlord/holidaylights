using System.Globalization;

namespace HolidayLights.App.Settings;

/// <summary>What the action link of a status line does.</summary>
public enum StatusAction
{
    /// <summary>No link.</summary>
    None,

    /// <summary>"Turn On": Show Lights.</summary>
    TurnOn,

    /// <summary>"Choose Displays": General.</summary>
    ChooseDisplays,

    /// <summary>"Change Bulbs": Bulb Factory.</summary>
    ChangeBulbs,

    /// <summary>"Change": General (When the Lights Rest).</summary>
    ChangeEnergySaver,

    /// <summary>"Try Again": retry the requested layer mode.</summary>
    TryAgain,
}

/// <summary>A status line and its action link.</summary>
/// <param name="Text">The sentence.</param>
/// <param name="ActionText">The link, or null.</param>
/// <param name="Action">What the link does.</param>
public sealed record StatusLine(string Text, string? ActionText = null, StatusAction Action = StatusAction.None);

/// <summary>
/// The status lines of PRODUCT-SPEC 3.1 (Home's "Lights" card; first matching row wins) and the fallback line of Bulb
/// Drawing (3.2.8) and General's "Where Bulbs Are Drawn".
/// </summary>
public static class LightsStatusText
{
    /// <summary>"Windows isn't letting Holiday Lights draw behind the icons right now, ...".</summary>
    public const string InFrontFallback =
        "Windows isn't letting Holiday Lights draw behind the icons right now, so the bulbs are in front of them. Holiday Lights keeps trying.";

    /// <summary>"The desktop isn't available right now, ...".</summary>
    public const string OnTopFallback =
        "The desktop isn't available right now, so the bulbs are on top of your windows. Holiday Lights keeps trying.";

    /// <summary>The fallback line while a fallback layer stands in for the requested one, else null.</summary>
    /// <param name="status">The presenter's status.</param>
    /// <returns>The line with "Try Again", or null.</returns>
    public static StatusLine? Fallback(LightsStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!status.IsFallbackActive)
        {
            return null;
        }

        bool onTop = status.Displays.Any(d => d.Effective == LayerMode.OnTop && status.Requested != LayerMode.OnTop);
        return new StatusLine(onTop ? OnTopFallback : InFrontFallback, "Try Again", StatusAction.TryAgain);
    }

    /// <summary>Home's status line (PRODUCT-SPEC 3.1; first matching row wins).</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="scene">The desktop scene.</param>
    /// <param name="status">The presenter's status.</param>
    /// <param name="next">The next Automatic theme change (shown while Automatic themes are on), or null.</param>
    /// <returns>The line.</returns>
    public static StatusLine Home(AppSettings settings, LightsScene scene, LightsStatus status, (DateOnly Date, string ThemeName)? next)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(status);
        if (!settings.Lights.On)
        {
            return new("The lights are off.", "Turn On", StatusAction.TurnOn);
        }

        if (scene.EnergySaverInEffect == EnergySaverChoice.TurnOffLights)
        {
            return new("The lights are off while Energy Saver is on.", "Change", StatusAction.ChangeEnergySaver);
        }

        if (scene.Displays.Count > 0 && scene.Displays.All(d => !d.Enabled))
        {
            return new("No display is set to show lights.", "Choose Displays", StatusAction.ChooseDisplays);
        }

        if (settings.Current.Arrangement.HasNoBulbs())
        {
            return new("No bulbs yet.", "Change Bulbs", StatusAction.ChangeBulbs);
        }

        if (status.Health == LightsHealth.NoGraphicsDevice)
        {
            return new("Your lights can't be drawn right now (no graphics device). Holiday Lights keeps trying.");
        }

        if (status.Paused.HasFlag(PauseReasons.RemoteSession))
        {
            return new("The lights rest during Remote Desktop sessions.");
        }

        IReadOnlyList<DisplayLayerStatus> enabled = status.Displays;
        if (enabled.Count > 0 && enabled.All(d => d.Resting) && status.Paused == PauseReasons.None)
        {
            return new("Resting while a full-screen app is open.");
        }

        if (enabled.Any(d => d.Resting) && status.Paused == PauseReasons.None)
        {
            int[] numbers = [.. enabled.Where(d => d.Resting).Select(d => NumberOf(scene, d.DisplayId)).Where(n => n > 0).Order()];
            string which = numbers.Length == 1
                ? $"Display {numbers[0]}"
                : "Displays " + Controls.ArrangementTexts.JoinWithAnd([.. numbers.Select(n => n.ToString(CultureInfo.CurrentCulture))]);
            return new($"Resting on {which} while a full-screen app is open.");
        }

        if (status.Paused.HasFlag(PauseReasons.Presentation))
        {
            return new("Resting during your presentation.");
        }

        if (scene.EnergySaverInEffect is EnergySaverChoice.UseLessPower or EnergySaverChoice.StopFlashing)
        {
            return new(scene.EnergySaverInEffect == EnergySaverChoice.StopFlashing
                ? "Not flashing while Energy Saver is on."
                : "Using less power while Energy Saver is on.", "Change", StatusAction.ChangeEnergySaver);
        }

        if (Fallback(status) is { } fallback)
        {
            return fallback;
        }

        int shown = Math.Max(1, scene.Displays.Count(d => d.Enabled));
        LayerMode mode = LayerModes.FromSettings(settings.Lights.Drawing, settings.Lights.BehindIcons);

        // "on" + "on top of all windows" would read "on on top": that row says "shining" instead.
        string state = mode == LayerMode.OnTop ? "shining" : "on";
        string text = $"Your lights are {state} {Location(mode)} on {shown} {(shown == 1 ? "display" : "displays")}.";
        if (settings.Calendar.Enabled && next is { } change)
        {
            text += $" Next: {change.ThemeName} on {ShortDate(change.Date)}.";
        }

        return new(text);
    }

    /// <summary>"behind your desktop icons", "in front of your desktop icons", "on top of all windows".</summary>
    /// <param name="mode">The layer mode.</param>
    /// <returns>The words.</returns>
    public static string Location(LayerMode mode) => mode switch
    {
        LayerMode.BehindIcons => "behind your desktop icons",
        LayerMode.InFrontOfIcons => "in front of your desktop icons",
        _ => "on top of all windows",
    };

    /// <summary>A short date in the Windows locale ("Nov 1").</summary>
    /// <param name="date">The date.</param>
    /// <returns>The text.</returns>
    public static string ShortDate(DateOnly date) => date.ToString("MMM d", CultureInfo.CurrentCulture);

    private static int NumberOf(LightsScene scene, string displayId) =>
        scene.Displays.FirstOrDefault(d => d.Display.DeviceId == displayId)?.Display.Number ?? 0;
}
