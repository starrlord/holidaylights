namespace HolidayLights.App.Tray;

/// <summary>What the tray tooltip describes.</summary>
/// <param name="LightsOn">"Show Lights" (the user's choice).</param>
/// <param name="Theme">"Automatic: Halloween", "Halloween" or "Custom Settings".</param>
/// <param name="Scene">The scene the lights show (the requested layer, the Energy Saver rule in effect).</param>
/// <param name="Status">The presenter's status (fallback modes).</param>
/// <param name="Pause">What rests.</param>
/// <param name="PlayingSong">The title of the song that plays, or null.</param>
public sealed record TrayTooltipState(bool LightsOn, string Theme, LightsScene Scene, LightsStatus Status, LightsPauseState Pause, string? PlayingSong)
{
    /// <summary>The music waits for the MIDI synthesizer another app holds (<c>MusicStatus.WaitingForSynthesizer</c>, PRODUCT-SPEC 3.4.6).</summary>
    public bool MusicWaiting { get; init; }
}

/// <summary>The tray tooltip of PRODUCT-SPEC 2.2 (up to 3 lines, at most 127 characters for the notification area). Pure.</summary>
public static class TrayTooltip
{
    /// <summary>The longest tooltip the notification area shows (<c>NOTIFYICONDATA.szTip</c> holds 128 characters).</summary>
    public const int MaxLength = 127;

    /// <summary>Line 3 while the music waits for the synthesizer (PRODUCT-SPEC 3.4.6).</summary>
    public const string MusicWaitingLine = "Music is waiting for the synthesizer";

    private const string Title = "Holiday Lights";
    private const string Ellipsis = "…";

    /// <summary>The tooltip text, lines separated by "\n".</summary>
    /// <param name="state">What to describe.</param>
    /// <returns>The text.</returns>
    public static string Build(TrayTooltipState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.LightsOn)
        {
            // The music does not follow "Show Lights", so a music problem is still told.
            return Title + "\nLights off" + (state.MusicWaiting ? "\n" + MusicWaitingLine : "");
        }

        string location = LocationText(state);
        int budget = MaxLength - Title.Length - 1;
        string text = Title + "\n" + Truncate(state.Theme, Math.Max(8, budget - location.Length - 3)) + " - " + location;
        if (state.MusicWaiting)
        {
            int left = MaxLength - text.Length - 1;
            return left >= 4 ? text + "\n" + Truncate(MusicWaitingLine, left) : text;
        }

        const string playing = "\nPlaying: ";
        int room = MaxLength - text.Length - playing.Length;
        return state.PlayingSong is { Length: > 0 } song && room >= 4 ? text + playing + Truncate(song, room) : text;
    }

    /// <summary>Where the lights are, in plain words: the effective place (a fallback included) or why they rest.</summary>
    /// <param name="state">What to describe.</param>
    /// <returns>E.g. "behind your icons" or "resting while a full-screen app is open".</returns>
    public static string LocationText(TrayTooltipState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        PauseReasons global = state.Pause.Global;
        if (global.HasFlag(PauseReasons.ExclusiveFullScreen) || state.Pause.RestingDisplayIds.Count > 0)
        {
            return "resting while a full-screen app is open";
        }

        if (global.HasFlag(PauseReasons.Presentation))
        {
            return "resting during a presentation";
        }

        if (state.Scene.EnergySaverInEffect == EnergySaverChoice.TurnOffLights)
        {
            return "resting while Energy Saver is on";
        }

        LayerMode? fallback = state.Status.Displays
            .Select(d => d.Effective)
            .FirstOrDefault(mode => mode is { } m && m != state.Scene.RequestedLayer);
        return (fallback ?? state.Scene.RequestedLayer) switch
        {
            LayerMode.BehindIcons => "behind your icons",
            LayerMode.InFrontOfIcons => "in front of your icons",
            _ => "on top of all windows",
        };
    }

    private static string Truncate(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        return length <= Ellipsis.Length ? "" : text[..(length - Ellipsis.Length)] + Ellipsis;
    }
}
