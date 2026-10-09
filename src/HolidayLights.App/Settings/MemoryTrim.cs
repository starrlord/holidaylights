using System.Runtime;
using HolidayLights.App.Preview;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.Settings;

/// <summary>
/// Gives back the memory the Settings window's previews used once it has closed (PRODUCT-SPEC 1.4 G5, 5.14 row 2: under
/// 200 MB with Settings closed; review r1 #19): the bulb objects and decoded art of the catalog, the scaled sprites in
/// memory, the thumbnail bitmaps and the wallpaper backdrops, then one compacting full collection. All of them are caches:
/// the desktop lights keep their own surfaces and the disk sprite cache, and anything needed again is rebuilt on demand.
/// </summary>
internal static class MemoryTrim
{
    private const string LogSource = "Settings";

    /// <summary>Trims the caches and collects (UI thread, after the window has closed).</summary>
    /// <param name="services">The application services.</param>
    public static void AfterSettingsClosed(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        long before = Environment.WorkingSet;
        (services.Bulbs as BulbCatalog)?.TrimMemory();
        (services.Sprites as SpriteProvider)?.TrimMemory();
        SpriteBitmaps.Clear();
        WallpaperBackdrops.Shared.Clear();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        services.Log.Info(LogSource, FormattableString.Invariant(
            $"Preview caches trimmed: working set {before / (1024 * 1024)} MB before, {Environment.WorkingSet / (1024 * 1024)} MB after."));
    }
}
