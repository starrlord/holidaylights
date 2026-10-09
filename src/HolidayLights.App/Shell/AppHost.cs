using System.Diagnostics;
using HolidayLights.App.Tray;
using HolidayLights.Audio;

namespace HolidayLights.App.Shell;

/// <summary>
/// The composition root (owner: app-shell): constructs every service of <see cref="IAppServices"/> in dependency order
/// (log, settings store, libraries, catalog, engines, platform services, presenter, music, window services), wires
/// settings to the scene builder, pause signals to the presenter and music, the instance pipe to commands, and runs the
/// start-up flow (first run, 5.4 import, power-up, Welcome card).
/// </summary>
/// <remarks>
/// <para>Services are created on first use, on the UI thread, so every session kind builds only what it needs: the normal
/// app all of them (in the order of CONTRACTS 6.1 during <see cref="StartAsync"/>), a settings-only session no lights,
/// tray, hot keys or music, a screen saver process what its saver touches, <c>--render-test</c> the lights engines only.
/// Members that make no sense in a session are passive there (no presenter, hot keys not registered, no tray).</para>
/// <para><see cref="DisposeAsync"/> is the orderly exit: the lights fade out (300 ms) while the music fades out (500 ms),
/// then the tray icon disappears, settings are written and everything stops in reverse order.</para>
/// <para>The host does not register itself in <see cref="AppServicesHost"/>: <see cref="Program"/> does that for the
/// session it runs, so hosts created elsewhere (tests) never replace the services of controls created by XAML.</para>
/// </remarks>
public sealed partial class AppHost : IAppServices, IAsyncDisposable
{
    private const string LogSource = "Shell";

    private readonly AppComponentFactory factory;
    private readonly bool ownsLog;
    private readonly TimeProvider time;
    private readonly Lazy<ISettingsStore> settings;
    private readonly Lazy<IShellOperations> shell;
    private readonly Lazy<IHoldingFolder> holding;
    private readonly Lazy<ISystemInfo> systemInfo;
    private readonly Lazy<IDisplayService> displays;
    private readonly Lazy<IWallpaperProvider> wallpapers;
    private readonly Lazy<IStartupRegistration> startup;
    private readonly Lazy<IFileAssociation> fileAssociation;
    private readonly Lazy<IScreenSaverRegistration> screenSaverRegistration;
    private readonly Lazy<IBulbCatalog> bulbs;
    private readonly Lazy<ISongLibrary> songs;
    private readonly Lazy<IPictureLibrary> pictures;
    private readonly Lazy<IThemeLibrary> themes;
    private readonly Lazy<IThemeService> themeService;
    private readonly Lazy<ISeasonCalendar> calendar;
    private readonly Lazy<ILegacyImporter> legacyImporter;
    private readonly Lazy<ILegacyLeftovers> legacyLeftovers;
    private readonly Lazy<ILayoutEngine> layout;
    private readonly Lazy<IFlashEngine> flash;
    private readonly Lazy<ISpriteProvider> sprites;
    private readonly Lazy<ICpuCompositor> compositor;
    private readonly Lazy<IMusicDirector> music;
    private readonly Lazy<ScreenSaverSessions> saverSessions;
    private readonly Lazy<PauseMonitor?> pauseMonitor;
    private readonly Lazy<LightsController> lights;
    private readonly Lazy<HotKeyController> hotKeys;
    private readonly Lazy<NotificationService> notifications;
    private readonly Lazy<AppShellCommands> appShell;
    private readonly Lazy<ISettingsWindowService> settingsWindow;
    private readonly Lazy<IBulbFactoryDialogs> bulbFactory;
    private readonly Lazy<IScreenSaverService> screenSaver;
    private Task catalogIndexing = Task.CompletedTask;
    private SettingsOnlyMusic? settingsOnlyMusic;

    /// <summary>Creates the services of a session (nothing starts yet).</summary>
    /// <param name="paths">Data paths (honouring <c>--data-root</c> / <c>HOLIDAYLIGHTS_DATA_ROOT</c>).</param>
    /// <param name="options">Session kind and <c>--no-system-changes</c>.</param>
    public AppHost(DataPaths paths, AppRuntimeOptions options)
        : this(paths, options, new RollingFileLog(paths), ownsLog: true, new AppComponentFactory(), TimeProvider.System, null)
    {
    }

    /// <summary>Creates the services of a session with an existing log and the single instance claimed by <see cref="Program"/>.</summary>
    /// <param name="paths">Data paths.</param>
    /// <param name="options">Session kind and <c>--no-system-changes</c>.</param>
    /// <param name="log">The log (owned by the caller).</param>
    /// <param name="instance">The claimed single instance whose pipe this session serves, or null.</param>
    internal AppHost(DataPaths paths, AppRuntimeOptions options, IAppLog log, ISingleInstance? instance)
        : this(paths, options, log, ownsLog: false, new AppComponentFactory(), TimeProvider.System, instance)
    {
    }

    /// <summary>Creates the services with explicit components (tests).</summary>
    /// <param name="paths">Data paths.</param>
    /// <param name="options">Session kind and <c>--no-system-changes</c>.</param>
    /// <param name="log">The log.</param>
    /// <param name="ownsLog">True when the host disposes the log.</param>
    /// <param name="factory">Creates the components.</param>
    /// <param name="time">The clock.</param>
    /// <param name="instance">The claimed single instance, or null.</param>
    internal AppHost(DataPaths paths, AppRuntimeOptions options, IAppLog log, bool ownsLog, AppComponentFactory factory, TimeProvider time, ISingleInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(time);
        Paths = paths;
        Options = options;
        Log = log;
        this.ownsLog = ownsLog;
        this.factory = factory;
        this.time = time;
        singleInstance = instance;

        settings = Create(() =>
        {
            ISettingsStore store = factory.CreateSettingsStore(this);
            store.SaveFailed += (_, e) => Log.Warn(LogSource, "The settings could not be saved.", e.Exception);
            Log.Info(LogSource, $"Settings {store.LoadOutcome}.");
            return store;
        });
        shell = Create(() => factory.CreateShell(this));
        holding = Create(() => factory.CreateHoldingFolder(this));
        systemInfo = Create(() => factory.CreateSystemInfo(this));
        displays = Create(() => factory.CreateDisplays(this));
        wallpapers = Create(() => factory.CreateWallpapers(this));
        startup = Create(() => factory.CreateStartup(this));
        fileAssociation = Create(() => factory.CreateFileAssociation(this));
        screenSaverRegistration = Create(() => factory.CreateScreenSaverRegistration(this));
        bulbs = Create(() =>
        {
            IBulbCatalog catalog = factory.CreateCatalog(this);
            catalogIndexing = catalog.StartAsync();
            return catalog;
        });
        songs = Create(() =>
        {
            ISongLibrary library = factory.CreateSongs(this);
            library.Start();
            return library;
        });
        pictures = Create(() =>
        {
            IPictureLibrary library = factory.CreatePictures(this);
            library.Start();
            return library;
        });
        themes = Create(() =>
        {
            IThemeLibrary library = factory.CreateThemes(this);
            library.Load();
            return library;
        });
        themeService = Create(() => factory.CreateThemeService(this));
        calendar = Create(() => factory.CreateCalendar(this));
        legacyImporter = Create(() => factory.CreateLegacyImporter(this));
        legacyLeftovers = Create(() => factory.CreateLegacyLeftovers(this));
        layout = Create(() => factory.CreateLayout(this));
        flash = Create(() => factory.CreateFlash(this));
        sprites = Create(() => factory.CreateSprites(this));
        compositor = Create(() => factory.CreateCompositor(this));
        music = Create(() =>
        {
            IMusicDirector director = factory.CreateMusic(this);
            if (Options.Session == AppSessionKind.SettingsOnly)
            {
                // No music of its own, but what the Music Box plays follows Volume, Mute and MIDI Output.
                settingsOnlyMusic = new SettingsOnlyMusic(Settings, director);
            }

            return director;
        });
        saverSessions = Create(() => new ScreenSaverSessions(this));
        pauseMonitor = Create(() => IsNormal ? new PauseMonitor(factory.CreatePauseSignals(this), Settings, saverSessions.Value) : null);
        lights = Create(() => IsNormal
            ? new LightsController(this, pauseMonitor.Value, factory.CreatePresenter(this), ownsPresenter: true)
            : new LightsController(this, null, null, ownsPresenter: false));
        hotKeys = Create(() =>
        {
            LightsController controller = lights.Value;
            return new HotKeyController(this, factory.CreateHotKeyService(this), controller.ShowPill);
        });
        notifications = Create(() => new NotificationService(this, (title, text) => tray?.ShowNotification(title, text) ?? false, () => pauseMonitor.Value?.Current ?? PauseState.None, time));
        settingsWindow = Create(() => factory.CreateSettingsWindow(this));
        bulbFactory = Create(() => factory.CreateBulbFactory(this));
        screenSaver = Create(() => factory.CreateScreenSaver(this));
        appShell = Create(() => new AppShellCommands(this, () => DisposeAsync().AsTask(), () => bulbFactory.IsValueCreated ? bulbFactory.Value : null));
    }

    /// <inheritdoc />
    public DataPaths Paths { get; }

    /// <inheritdoc />
    public AppRuntimeOptions Options { get; }

    /// <inheritdoc />
    public IAppLog Log { get; }

    /// <inheritdoc />
    public ISettingsStore Settings => settings.Value;

    /// <inheritdoc />
    public IThemeLibrary Themes => themes.Value;

    /// <inheritdoc />
    public IThemeService ThemeService => themeService.Value;

    /// <inheritdoc />
    public ISeasonCalendar Calendar => calendar.Value;

    /// <inheritdoc />
    public ILegacyImporter LegacyImporter => legacyImporter.Value;

    /// <inheritdoc />
    public ILegacyLeftovers LegacyLeftovers => legacyLeftovers.Value;

    /// <inheritdoc />
    public IBulbCatalog Bulbs => bulbs.Value;

    /// <inheritdoc />
    public ILayoutEngine Layout => layout.Value;

    /// <inheritdoc />
    public IFlashEngine Flash => flash.Value;

    /// <inheritdoc />
    public ISpriteProvider Sprites => sprites.Value;

    /// <inheritdoc />
    public ICpuCompositor Compositor => compositor.Value;

    /// <inheritdoc />
    public ISongLibrary Songs => songs.Value;

    /// <inheritdoc />
    public IMusicDirector Music => music.Value;

    /// <inheritdoc />
    public IPictureLibrary Pictures => pictures.Value;

    /// <inheritdoc />
    public IDisplayService Displays => displays.Value;

    /// <inheritdoc />
    public IWallpaperProvider Wallpapers => wallpapers.Value;

    /// <inheritdoc />
    public ISystemInfo SystemInfo => systemInfo.Value;

    /// <inheritdoc />
    public IShellOperations Shell => shell.Value;

    /// <inheritdoc />
    public IHoldingFolder Holding => holding.Value;

    /// <inheritdoc />
    public IStartupRegistration Startup => startup.Value;

    /// <inheritdoc />
    public IFileAssociation FileAssociation => fileAssociation.Value;

    /// <inheritdoc />
    public IScreenSaverRegistration ScreenSaverRegistration => screenSaverRegistration.Value;

    /// <inheritdoc />
    public ILightsController Lights => lights.Value;

    /// <inheritdoc />
    public IHotKeyController HotKeys => hotKeys.Value;

    /// <inheritdoc />
    public INotificationService Notifications => notifications.Value;

    /// <inheritdoc />
    public IAppShell AppShell => appShell.Value;

    /// <inheritdoc />
    public IScreenSaverSessions SaverSessions => saverSessions.Value;

    /// <inheritdoc />
    public ISettingsWindowService SettingsWindow => settingsWindow.Value;

    /// <inheritdoc />
    public IUndoHistory Undo => SettingsWindow.Undo;

    /// <inheritdoc />
    public IBulbFactoryDialogs BulbFactory => bulbFactory.Value;

    /// <inheritdoc />
    public IScreenSaverService ScreenSaver => screenSaver.Value;

    /// <summary>The task of the add-on bulb index (complete when every bundled and My Bulbs file is indexed).</summary>
    public Task CatalogIndexing
    {
        get
        {
            _ = Bulbs;
            return catalogIndexing;
        }
    }

    /// <summary>When the process started (<see cref="Stopwatch.GetTimestamp"/>), for the time-to-first-frame log.</summary>
    public long ProcessStartTimestamp { get; init; } = Stopwatch.GetTimestamp();

    private bool IsNormal => Options.Session == AppSessionKind.Normal;

    private static Lazy<T> Create<T>(Func<T> create) => new(create, LazyThreadSafetyMode.ExecutionAndPublication);
}
