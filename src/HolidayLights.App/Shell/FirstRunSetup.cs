using HolidayLights.App.FirstRun;
using HolidayLights.Core.Settings;

namespace HolidayLights.App.Shell;

/// <summary>What the first run decided.</summary>
/// <param name="Variant">The Welcome card to show.</param>
/// <param name="HoldFirstSong">The first song waits for the Welcome card to close (5.4 imports, PRODUCT-SPEC 2.5.3).</param>
public sealed record FirstRunOutcome(WelcomeVariant Variant, bool HoldFirstSong);

/// <summary>
/// The first-run settings (PRODUCT-SPEC 2.5, 6.8.4; CONTRACTS 6.1 step 5): the newcomer settings, or the Holiday Lights 5.4
/// import on top of them when 5.4 is present (factory defaults keep the newcomer look and add "Holiday Lights 5.4
/// Settings" to Recent Settings; a customized 5.4 is imported as it is). Applied before the first frame; reading the
/// registry and copying files run on the thread pool. A failed import falls back to the newcomer settings, so the lights
/// always come on.
/// </summary>
public sealed class FirstRunSetup
{
    private const string LogSource = "Shell.FirstRun";

    private readonly IAppServices services;
    private readonly TimeProvider time;

    /// <summary>Creates the first-run step.</summary>
    /// <param name="services">The services (settings, calendar, themes, the 5.4 importer, system facts).</param>
    /// <param name="time">The clock.</param>
    public FirstRunSetup(IAppServices services, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(time);
        this.services = services;
        this.time = time;
    }

    /// <summary>Applies the first-run settings (UI thread).</summary>
    /// <returns>The Welcome variant and whether the first song waits.</returns>
    public async Task<FirstRunOutcome> RunAsync()
    {
        DateTimeOffset now = time.GetLocalNow();
        ISystemInfo system = services.SystemInfo;
        var context = new FirstRunContext(DateOnly.FromDateTime(now.DateTime), system.RegionCode, system.AnimationsEnabled, now);
        AppSettings newcomer = NewcomerSettings.Create(services.Settings.Current, context, services.Calendar, services.Themes, services.ThemeService);

        ILegacyImporter importer = services.LegacyImporter;
        try
        {
            LegacyImportPreview? preview = await Task.Run(() => importer.IsLegacyInstallPresent() ? importer.Analyze() : null).ConfigureAwait(true);
            if (preview is not null)
            {
                LegacyImportResult result = await Task.Run(() => importer.Import(preview, newcomer, LegacyImportMode.FirstRun)).ConfigureAwait(true);
                services.Settings.Update(_ => result.Settings, new SettingsChange(SettingsChangeKind.Import, "Import the Holiday Lights 5.4 settings"));
                services.Log.Info(LogSource,
                    $"Imported Holiday Lights 5.4 ({(preview.IsFactoryDefault ? "factory defaults" : "customized")}): {result.Record.Themes} themes, " +
                    $"{result.Record.Bulbs} bulbs, {result.Record.Songs} songs, {result.Record.Pictures} pictures.");
                return new FirstRunOutcome(
                    preview.IsFactoryDefault ? WelcomeVariant.Imported54FactoryDefaults : WelcomeVariant.Imported54Customized,
                    HoldFirstSong: true);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The Holiday Lights 5.4 import failed; starting with the newcomer settings.", e);
        }

        services.Settings.Update(_ => newcomer, SettingsChange.Internal);
        services.Log.Info(LogSource, $"First run: newcomer settings with the {newcomer.Themes.LastName} theme.");
        return new FirstRunOutcome(WelcomeVariant.Newcomer, HoldFirstSong: false);
    }
}
