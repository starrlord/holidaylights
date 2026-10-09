using System.IO;
using System.Security;

namespace HolidayLights.App.FirstRun;

/// <summary>The facts the Welcome card shows lines for (PRODUCT-SPEC 3.11, 6.8.3), read before it opens.</summary>
/// <param name="Leftovers">Holiday Lights 5.4 still running or starting with Windows (5.4 variants only), or null.</param>
/// <param name="ScreenSaver">Which screen saver Windows uses.</param>
public sealed record WelcomeSituation(LegacyLeftoverState? Leftovers, ScreenSaverState ScreenSaver)
{
    /// <summary>Reads the facts (registry and Startup folder; call it off the UI thread).</summary>
    /// <param name="services">The services.</param>
    /// <param name="variant">The Welcome variant (5.4 leftovers are only looked for after a 5.4 import).</param>
    /// <returns>The facts; what cannot be read counts as absent.</returns>
    public static WelcomeSituation Detect(IAppServices services, WelcomeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(services);
        LegacyLeftoverState? leftovers = null;
        ScreenSaverState saver = ScreenSaverState.NotOurs;
        try
        {
            if (variant != WelcomeVariant.Newcomer)
            {
                leftovers = services.LegacyLeftovers.Detect();
            }

            saver = services.ScreenSaverRegistration.GetStatus().State;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            services.Log.Warn("FirstRun", "The Holiday Lights 5.4 leftovers could not be checked.", e);
        }

        return new WelcomeSituation(leftovers, saver);
    }

    /// <summary>"Its 11 themes are included." for the factory-default line: the 5.4 themes the import took over or found already included.</summary>
    /// <param name="record">The import record.</param>
    /// <returns>The sentence (with a leading space), or empty when 5.4 had no themes.</returns>
    public static string ThemesSentence(Import54Record? record)
    {
        int themes = record?.Report.Count(item => item.Item.StartsWith(@"Themes\", StringComparison.OrdinalIgnoreCase)
            && item.Status != ImportItemStatus.NotImported) ?? 0;
        return themes switch
        {
            0 => "",
            1 => " Its theme is included.",
            _ => $" Its {themes} themes are included.",
        };
    }
}
