using HolidayLights.App.BulbFactory;
using HolidayLights.App.ScreenSaver;
using HolidayLights.App.Settings;
using HolidayLights.Audio;
using HolidayLights.Audio.Library;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Flash;
using HolidayLights.Core.Layout;
using HolidayLights.Core.Legacy;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Settings;
using HolidayLights.Core.Sprites;
using HolidayLights.Core.Themes;
using HolidayLights.Platform.Displays;
using HolidayLights.Platform.Files;
using HolidayLights.Platform.Input;
using HolidayLights.Platform.Integration;
using HolidayLights.Platform.Legacy;
using HolidayLights.Platform.Power;
using HolidayLights.Platform.Shell;
using HolidayLights.Rendering;

namespace HolidayLights.App.Shell;

/// <summary>
/// Creates the components of the composition root (CONTRACTS 6.1): the real implementations of every owner. Each
/// method receives the host, whose properties give the dependencies created before. Tests derive from it to substitute
/// fakes for single components.
/// </summary>
internal class AppComponentFactory
{
    /// <summary>The settings store (<c>settings.json</c>).</summary>
    public virtual ISettingsStore CreateSettingsStore(AppHost host) => JsonSettingsStore.Load(host.Paths, host.Log);

    /// <summary>Explorer, URIs, Recycle Bin, shortcuts.</summary>
    public virtual IShellOperations CreateShell(AppHost host) => new ShellOperations(host.Log);

    /// <summary>The removal holding folder.</summary>
    public virtual IHoldingFolder CreateHoldingFolder(AppHost host) => new HoldingFolder(host.Paths, host.Shell, host.Log);

    /// <summary>Facts about Windows (UI thread).</summary>
    public virtual ISystemInfo CreateSystemInfo(AppHost host) => new SystemInfo(host.Log);

    /// <summary>Displays and their changes (UI thread).</summary>
    public virtual IDisplayService CreateDisplays(AppHost host) => new DisplayService(host.Log);

    /// <summary>Wallpapers for light stages.</summary>
    public virtual IWallpaperProvider CreateWallpapers(AppHost host) => new WallpaperProvider(host.Log);

    /// <summary>The Run value.</summary>
    public virtual IStartupRegistration CreateStartup(AppHost host) => new StartupRegistration(host.Paths, host.Options, host.Log);

    /// <summary>The <c>.bul</c> association.</summary>
    public virtual IFileAssociation CreateFileAssociation(AppHost host) => new FileAssociation(host.Paths, host.Options, host.Log);

    /// <summary>The Windows screen saver configuration.</summary>
    public virtual IScreenSaverRegistration CreateScreenSaverRegistration(AppHost host) =>
        new ScreenSaverRegistration(host.Paths, host.Shell, host.Options, host.Log);

    /// <summary>The bulb catalog; a bulb made from a GIF is credited to the Windows account display name.</summary>
    public virtual IBulbCatalog CreateCatalog(AppHost host) =>
        new BulbCatalog(host.Paths, host.Settings, host.Holding, host.Log, () => host.SystemInfo.UserDisplayName);

    /// <summary>The Music Box songs.</summary>
    public virtual ISongLibrary CreateSongs(AppHost host) => new MusicLibrary(host.Paths, host.Settings, host.Holding, host.Shell, host.Log);

    /// <summary>The screen saver pictures.</summary>
    public virtual IPictureLibrary CreatePictures(AppHost host) => new PictureLibrary(host.Paths, host.Settings, host.Holding, host.Log);

    /// <summary>The theme files.</summary>
    public virtual IThemeLibrary CreateThemes(AppHost host) => new ThemeLibrary(host.Paths, host.Holding, host.Log);

    /// <summary>Theme load, capture and match.</summary>
    public virtual IThemeService CreateThemeService(AppHost host) => new ThemeService(host.Themes, host.Songs, host.Bulbs);

    /// <summary>The Theme Calendar rules.</summary>
    public virtual ISeasonCalendar CreateCalendar(AppHost host) => new SeasonCalendar();

    /// <summary>The Holiday Lights 5.4 import (reads the 5.4 registry, never writes it).</summary>
    public virtual ILegacyImporter CreateLegacyImporter(AppHost host) => new LegacyImporter(
        new LegacyRegistryReader(host.Shell, host.Log), host.Bulbs, host.Songs, host.Pictures, host.Themes, host.ThemeService, host.Shell, host.Log);

    /// <summary>Holiday Lights 5.4 leftovers.</summary>
    public virtual ILegacyLeftovers CreateLegacyLeftovers(AppHost host) => new LegacyLeftovers(host.Shell, host.Options, host.Log);

    /// <summary>The layout engine.</summary>
    public virtual ILayoutEngine CreateLayout(AppHost host) => new ClassicLayoutEngine();

    /// <summary>The flash patterns.</summary>
    public virtual IFlashEngine CreateFlash(AppHost host) => new FlashEngine();

    /// <summary>Scaled sprites and glow.</summary>
    public virtual ISpriteProvider CreateSprites(AppHost host) => new SpriteProvider(host.Paths, host.Log);

    /// <summary>The CPU compositor of previews.</summary>
    public virtual ICpuCompositor CreateCompositor(AppHost host) => new CpuCompositor();

    /// <summary>The Music Box engine.</summary>
    public virtual IMusicDirector CreateMusic(AppHost host) => new MusicDirector(host.Songs, host.Log);

    /// <summary>The OS pause signals (normal sessions).</summary>
    public virtual IPauseSignalSource CreatePauseSignals(AppHost host) => new PauseSignalSource(host.Displays, host.Log);

    /// <summary>The desktop lights (normal sessions).</summary>
    public virtual LightsPresenter CreatePresenter(AppHost host) => LightsController.CreatePresenter(host);

    /// <summary>Global hot keys (UI thread).</summary>
    public virtual IHotKeyService CreateHotKeyService(AppHost host) => new HotKeyService(host.Log);

    /// <summary>The Settings window.</summary>
    public virtual ISettingsWindowService CreateSettingsWindow(AppHost host) => new SettingsWindowService(host);

    /// <summary>Bulb Editing and the bulb dialogs.</summary>
    public virtual IBulbFactoryDialogs CreateBulbFactory(AppHost host) => new BulbFactoryDialogs(host);

    /// <summary>The screen saver for the Settings window.</summary>
    public virtual IScreenSaverService CreateScreenSaver(AppHost host) => new ScreenSaverService(host);
}
