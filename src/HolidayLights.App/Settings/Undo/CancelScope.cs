namespace HolidayLights.App.Settings.Undo;

/// <summary>
/// The Cancel scope of the Settings window (PRODUCT-SPEC 2.3): everything except "Show Lights" and the music transport.
/// Cancel puts the snapshot taken when the window opened back as a whole; bookkeeping that is not a user's choice in
/// this window keeps its current value.
/// </summary>
public static class CancelScope
{
    /// <summary>
    /// The snapshot with the values that Cancel never restores taken from the current settings: "Show Lights", the
    /// Settings window state (placement, last page) and the remembered Bulb List view and sort, the onboarding flags and
    /// notification throttling, the 5.4 import record (it describes files that stay) and the version.
    /// </summary>
    /// <param name="current">The settings now.</param>
    /// <param name="snapshot">The settings when the window opened.</param>
    /// <returns>The settings Cancel applies, or <paramref name="current"/> when they are already equal.</returns>
    public static AppSettings Restore(AppSettings current, AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(snapshot);
        AppSettings restored = snapshot with
        {
            Version = current.Version,
            Lights = snapshot.Lights with { On = current.Lights.On },
            Ui = snapshot.Ui with { Settings = current.Ui.Settings, Gallery = current.Ui.Gallery },
            Onboarding = current.Onboarding,
            Import54 = current.Import54,
        };
        return SettingsMerge.Apply(current, restored, current);
    }
}
