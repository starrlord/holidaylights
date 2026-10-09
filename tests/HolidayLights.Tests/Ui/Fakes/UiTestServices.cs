using HolidayLights.App.Settings;
using HolidayLights.Audio;
using HolidayLights.Audio.Library;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Flash;
using HolidayLights.Core.Layout;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Sprites;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>
/// The design-time and test service set of the Settings window: the real Core engines and libraries (catalog with the
/// bundled bulbs, layout, flash, sprites, compositor, themes, calendar, songs) in a private data root, and fakes for
/// everything that changes Windows or belongs to owners built in parallel (scene builder, hot keys, notifications,
/// screen saver, Bulb Editing, music engine). Create it on the STA thread that will host the windows.
/// </summary>
public sealed class UiTestServices : IAppServices, IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly BulbCatalog catalog;
    private readonly SettingsWindowService settingsWindow;

    /// <summary>Creates the services.</summary>
    /// <param name="initial">The initial settings, or null for the defaults.</param>
    /// <param name="displays">The displays, or null for the reference PC.</param>
    /// <param name="wallpaper">A wallpaper picture for the stages, or null for the night gradient.</param>
    public UiTestServices(AppSettings? initial = null, IReadOnlyList<DisplayInfo>? displays = null, string? wallpaper = null)
    {
        Paths = root.Paths;
        Store = new InMemorySettingsStore(initial);
        Holding = new TestHoldingFolder(Paths);
        catalog = new BulbCatalog(Paths, Store, Holding, Log, () => "Pat Smith");
        IndexTask = catalog.StartAsync();
        ShellFake = new FakeShell();
        var songs = new MusicLibrary(Paths, Store, Holding, ShellFake, Log);
        songs.Start();
        Songs = songs;
        var themes = new ThemeLibrary(Paths, Holding, Log);
        themes.Load();
        Themes = themes;
        ThemeService = new ThemeService(themes, songs, catalog);
        DisplaysFake = new FakeDisplayService(displays);
        Wallpapers = new FakeWallpaperProvider(wallpaper);
        LightsFake = new FakeLightsController(Store, DisplaysFake, Layout, catalog);
        HotKeysFake = new FakeHotKeyController(Store);
        MusicFake = new FakeMusicDirector();
        PicturesFake = new FakePictureLibrary(Paths, Store);
        BulbFactoryFake = new FakeBulbFactoryDialogs(catalog);
        settingsWindow = new SettingsWindowService(this);
    }

    /// <summary>Completes when every add-on bulb is indexed.</summary>
    public Task IndexTask { get; }

    /// <summary>The in-memory settings store.</summary>
    public InMemorySettingsStore Store { get; }

    /// <summary>The fake displays.</summary>
    public FakeDisplayService DisplaysFake { get; }

    /// <summary>The fake scene builder.</summary>
    public FakeLightsController LightsFake { get; }

    /// <summary>The fake hot keys.</summary>
    public FakeHotKeyController HotKeysFake { get; }

    /// <summary>The fake music engine.</summary>
    public FakeMusicDirector MusicFake { get; }

    /// <summary>The fake picture library.</summary>
    public FakePictureLibrary PicturesFake { get; }

    /// <summary>The fake shell.</summary>
    public FakeShell ShellFake { get; }

    /// <summary>The fake bulb dialogs.</summary>
    public FakeBulbFactoryDialogs BulbFactoryFake { get; }

    /// <summary>The fake notifications.</summary>
    public FakeNotifications NotificationsFake { get; } = new();

    /// <summary>The fake application commands.</summary>
    public FakeAppShell AppShellFake { get; } = new();

    /// <summary>The fake system facts.</summary>
    public FakeSystemInfo SystemInfoFake { get; } = new();

    /// <summary>The fake screen saver registration.</summary>
    public FakeScreenSaverRegistration SaverRegistrationFake { get; } = new();

    /// <summary>The fake Run value.</summary>
    public FakeStartup StartupFake { get; } = new();

    /// <summary>The fake <c>.bul</c> association.</summary>
    public FakeFileAssociation AssociationFake { get; } = new();

    /// <summary>The fake 5.4 importer.</summary>
    public FakeLegacyImporter LegacyImporterFake { get; } = new();

    /// <summary>The fake 5.4 leftovers.</summary>
    public FakeLegacyLeftovers LegacyLeftoversFake { get; } = new();

    /// <summary>The test holding folder.</summary>
    public TestHoldingFolder Holding { get; }

    /// <summary>The recording log.</summary>
    public RecordingLog Log { get; } = new();

    /// <inheritdoc />
    public DataPaths Paths { get; }

    /// <inheritdoc />
    public AppRuntimeOptions Options { get; } = new() { AllowSystemChanges = false };

    /// <inheritdoc />
    IAppLog IAppServices.Log => Log;

    /// <inheritdoc />
    public ISettingsStore Settings => Store;

    /// <inheritdoc />
    public IThemeLibrary Themes { get; }

    /// <inheritdoc />
    public IThemeService ThemeService { get; }

    /// <inheritdoc />
    public ISeasonCalendar Calendar { get; } = new SeasonCalendar();

    /// <inheritdoc />
    public ILegacyImporter LegacyImporter => LegacyImporterFake;

    /// <inheritdoc />
    public ILegacyLeftovers LegacyLeftovers => LegacyLeftoversFake;

    /// <inheritdoc />
    public IBulbCatalog Bulbs => CatalogOverride ?? catalog;

    /// <summary>A catalog that stands in for the real one (set before any window is created), or null.</summary>
    public IBulbCatalog? CatalogOverride { get; set; }

    /// <inheritdoc />
    public ILayoutEngine Layout { get; } = new ClassicLayoutEngine();

    /// <inheritdoc />
    public IFlashEngine Flash { get; } = new FlashEngine();

    /// <inheritdoc />
    public ISpriteProvider Sprites => sprites ??= new SpriteProvider(Paths, Log);

    /// <inheritdoc />
    public ICpuCompositor Compositor { get; } = new CpuCompositor();

    /// <inheritdoc />
    public ISongLibrary Songs { get; }

    /// <inheritdoc />
    public IMusicDirector Music => MusicFake;

    /// <inheritdoc />
    public IPictureLibrary Pictures => PicturesFake;

    /// <inheritdoc />
    public IDisplayService Displays => DisplaysFake;

    /// <inheritdoc />
    public IWallpaperProvider Wallpapers { get; }

    /// <inheritdoc />
    public ISystemInfo SystemInfo => SystemInfoFake;

    /// <inheritdoc />
    public IShellOperations Shell => ShellFake;

    /// <inheritdoc />
    IHoldingFolder IAppServices.Holding => Holding;

    /// <inheritdoc />
    public IStartupRegistration Startup => StartupFake;

    /// <inheritdoc />
    public IFileAssociation FileAssociation => AssociationFake;

    /// <inheritdoc />
    public IScreenSaverRegistration ScreenSaverRegistration => SaverRegistrationFake;

    /// <inheritdoc />
    public ILightsController Lights => LightsFake;

    /// <inheritdoc />
    public IHotKeyController HotKeys => HotKeysFake;

    /// <inheritdoc />
    public INotificationService Notifications => NotificationsFake;

    /// <inheritdoc />
    public IAppShell AppShell => AppShellFake;

    /// <inheritdoc />
    public IScreenSaverSessions SaverSessions { get; } = new FakeSaverSessions();

    /// <inheritdoc />
    public ISettingsWindowService SettingsWindow => settingsWindow;

    /// <summary>The real Settings window service.</summary>
    public SettingsWindowService SettingsWindowService => settingsWindow;

    /// <inheritdoc />
    public IUndoHistory Undo => settingsWindow.Undo;

    /// <inheritdoc />
    public IBulbFactoryDialogs BulbFactory => BulbFactoryFake;

    /// <inheritdoc />
    public IScreenSaverService ScreenSaver { get; } = new FakeScreenSaverService();

    private ISpriteProvider? sprites;

    /// <summary>Stops the catalog and deletes the data root.</summary>
    public void Dispose()
    {
        settingsWindow.CloseKeepingChanges();
        catalog.Dispose();
        root.Dispose();
    }
}
