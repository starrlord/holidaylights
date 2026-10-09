using System.IO.Packaging;

namespace HolidayLights.App.Shell;

/// <summary>
/// Turns the application pack URIs of <see cref="AppAssets"/> (<c>pack://application:,,,/Assets/...</c>) into URIs that
/// name this assembly (<c>pack://application:,,,/HolidayLights;component/Assets/...</c>), so the art loads whichever
/// program hosts the windows (the app, the screen saver copy, a test host).
/// </summary>
internal static class AppAssetUris
{
    private const string ApplicationPrefix = "pack://application:,,,/";

    private static readonly string Component = $"{ApplicationPrefix}{typeof(AppAssetUris).Assembly.GetName().Name};component/";

    static AppAssetUris()
    {
        // Touching the pack scheme registers it with Uri even before a WPF Application exists.
        _ = PackUriHelper.UriSchemePack;
    }

    /// <summary>The assembly-qualified form of an application pack URI (other URIs are returned unchanged).</summary>
    /// <param name="packUri">A pack URI of <see cref="AppAssets"/>.</param>
    /// <returns>The URI.</returns>
    public static Uri Resolve(string packUri)
    {
        ArgumentException.ThrowIfNullOrEmpty(packUri);
        return packUri.StartsWith(ApplicationPrefix, StringComparison.OrdinalIgnoreCase) && !packUri.Contains(";component/", StringComparison.OrdinalIgnoreCase)
            ? new Uri(Component + packUri[ApplicationPrefix.Length..])
            : new Uri(packUri);
    }
}
