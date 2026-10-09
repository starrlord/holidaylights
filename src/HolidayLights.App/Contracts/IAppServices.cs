using HolidayLights.Audio;

namespace HolidayLights.App.Contracts;

/// <summary>
/// The composition root's service set (ARCHITECTURE 7). Implemented by app-shell (<c>Shell\AppHost</c>) for every session
/// kind (normal, settings-only, screen saver); members that make no sense in a session are passive there (for example
/// <see cref="Lights"/> builds scenes without a presenter in a settings-only session).
/// </summary>
/// <remarks>
/// Windows, pages and controls receive it by constructor or, when created by XAML, through <see cref="AppServicesHost"/>.
/// Unless a member says otherwise, use services from the UI thread.
/// </remarks>
public interface IAppServices
{
    /// <summary>Folders and files.</summary>
    DataPaths Paths { get; }

    /// <summary>Process-wide options (session kind, <c>--no-system-changes</c>).</summary>
    AppRuntimeOptions Options { get; }

    /// <summary>The application log.</summary>
    IAppLog Log { get; }

    /// <summary>Settings (core-settings).</summary>
    ISettingsStore Settings { get; }

    /// <summary>Theme files (core-settings).</summary>
    IThemeLibrary Themes { get; }

    /// <summary>Theme load, capture and match (core-settings).</summary>
    IThemeService ThemeService { get; }

    /// <summary>The Theme Calendar rules (core-settings).</summary>
    ISeasonCalendar Calendar { get; }

    /// <summary>The Holiday Lights 5.4 import (core-settings).</summary>
    ILegacyImporter LegacyImporter { get; }

    /// <summary>Holiday Lights 5.4 leftovers (core-settings).</summary>
    ILegacyLeftovers LegacyLeftovers { get; }

    /// <summary>Every bulb (core-bulbs).</summary>
    IBulbCatalog Bulbs { get; }

    /// <summary>The layout engine (core-layout).</summary>
    ILayoutEngine Layout { get; }

    /// <summary>Flash patterns (core-layout).</summary>
    IFlashEngine Flash { get; }

    /// <summary>Scaled sprites and glow (core-sprites).</summary>
    ISpriteProvider Sprites { get; }

    /// <summary>The CPU compositor for previews (core-sprites).</summary>
    ICpuCompositor Compositor { get; }

    /// <summary>The Music Box songs (audio).</summary>
    ISongLibrary Songs { get; }

    /// <summary>The Music Box engine (audio).</summary>
    IMusicDirector Music { get; }

    /// <summary>Screen saver pictures (screensaver).</summary>
    IPictureLibrary Pictures { get; }

    /// <summary>Displays and their changes (platform).</summary>
    IDisplayService Displays { get; }

    /// <summary>Wallpapers for light stages (platform).</summary>
    IWallpaperProvider Wallpapers { get; }

    /// <summary>Facts about Windows (platform).</summary>
    ISystemInfo SystemInfo { get; }

    /// <summary>Explorer, URIs, Recycle Bin, shortcuts (platform).</summary>
    IShellOperations Shell { get; }

    /// <summary>The removal holding folder (platform).</summary>
    IHoldingFolder Holding { get; }

    /// <summary>The Run value (platform).</summary>
    IStartupRegistration Startup { get; }

    /// <summary>The <c>.bul</c> association (platform).</summary>
    IFileAssociation FileAssociation { get; }

    /// <summary>The Windows screen saver configuration (platform).</summary>
    IScreenSaverRegistration ScreenSaverRegistration { get; }

    /// <summary>The desktop lights: scene, status, clock (app-shell).</summary>
    ILightsController Lights { get; }

    /// <summary>Hot key registration state (app-shell).</summary>
    IHotKeyController HotKeys { get; }

    /// <summary>Tray notifications (app-shell).</summary>
    INotificationService Notifications { get; }

    /// <summary>About, Help, Exit, Uninstall (app-shell).</summary>
    IAppShell AppShell { get; }

    /// <summary>Screen saver sessions that make the desktop rest and music follow the saver (app-shell).</summary>
    IScreenSaverSessions SaverSessions { get; }

    /// <summary>The Settings window (settings-ui).</summary>
    ISettingsWindowService SettingsWindow { get; }

    /// <summary>The active Undo history (settings-ui; a no-op history while Settings is closed).</summary>
    IUndoHistory Undo { get; }

    /// <summary>Bulb Editing and the bulb dialogs (bulb-factory).</summary>
    IBulbFactoryDialogs BulbFactory { get; }

    /// <summary>Screen saver preview and "Preview Screen Saver" (screensaver).</summary>
    IScreenSaverService ScreenSaver { get; }
}

/// <summary>
/// Access to the services for objects created by XAML (controls, pages without constructor arguments). Set once at start-up
/// by app-shell; tests may set it to a fake.
/// </summary>
public static class AppServicesHost
{
    private static IAppServices? current;

    /// <summary>True after <see cref="Initialize"/>.</summary>
    public static bool IsAvailable => current is not null;

    /// <summary>The services.</summary>
    /// <exception cref="InvalidOperationException">Not initialized.</exception>
    public static IAppServices Current => current ?? throw new InvalidOperationException("AppServicesHost has not been initialized.");

    /// <summary>Sets the services (app-shell at start-up; tests).</summary>
    /// <param name="services">The services.</param>
    public static void Initialize(IAppServices services) => current = services ?? throw new ArgumentNullException(nameof(services));
}
