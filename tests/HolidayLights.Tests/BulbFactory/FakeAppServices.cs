using HolidayLights.Audio;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>The application services the Bulb Factory dialogs use, from a test bed; every other service is not available.</summary>
internal sealed class FakeAppServices(EditorTestBed bed) : IAppServices
{
    /// <summary>The lights (their scene sets the preview speed).</summary>
    public FakeLights FakeLights { get; } = new();

    /// <summary>Folders opened with "Show in Folder".</summary>
    public FakeShell FakeShell { get; } = new();

    public DataPaths Paths => bed.Paths;

    public IAppLog Log => bed.Log;

    public ISettingsStore Settings => bed.Settings;

    public IBulbCatalog Bulbs => bed.Catalog;

    public IHoldingFolder Holding => bed.Holding;

    public IUndoHistory Undo => bed.Undo;

    public ILightsController Lights => FakeLights;

    public IShellOperations Shell => FakeShell;

    public AppRuntimeOptions Options => throw NotAvailable();

    public IThemeLibrary Themes => throw NotAvailable();

    public IThemeService ThemeService => throw NotAvailable();

    public ISeasonCalendar Calendar => throw NotAvailable();

    public ILegacyImporter LegacyImporter => throw NotAvailable();

    public ILegacyLeftovers LegacyLeftovers => throw NotAvailable();

    public ILayoutEngine Layout => throw NotAvailable();

    public IFlashEngine Flash => throw NotAvailable();

    public ISpriteProvider Sprites => throw NotAvailable();

    public ICpuCompositor Compositor => throw NotAvailable();

    public ISongLibrary Songs => throw NotAvailable();

    public IMusicDirector Music => throw NotAvailable();

    public IPictureLibrary Pictures => throw NotAvailable();

    public IDisplayService Displays => throw NotAvailable();

    public IWallpaperProvider Wallpapers => throw NotAvailable();

    public ISystemInfo SystemInfo => throw NotAvailable();

    public IStartupRegistration Startup => throw NotAvailable();

    public IFileAssociation FileAssociation => throw NotAvailable();

    public IScreenSaverRegistration ScreenSaverRegistration => throw NotAvailable();

    public IHotKeyController HotKeys => throw NotAvailable();

    public INotificationService Notifications => throw NotAvailable();

    public IAppShell AppShell => throw NotAvailable();

    public IScreenSaverSessions SaverSessions => throw NotAvailable();

    public ISettingsWindowService SettingsWindow => throw NotAvailable();

    public IBulbFactoryDialogs BulbFactory => throw NotAvailable();

    public IScreenSaverService ScreenSaver => throw NotAvailable();

    private static NotSupportedException NotAvailable() => new("This service is not part of the Bulb Factory tests.");
}

/// <summary>Lights that only have a scene.</summary>
internal sealed class FakeLights : ILightsController
{
    public event EventHandler? SceneChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? StatusChanged
    {
        add { }
        remove { }
    }

    public LightsScene Scene { get; set; } = LightsScene.Empty;

    public LightsStatus Status => throw new NotSupportedException();

    public IStepClockSource Clock => throw new NotSupportedException();

    public void SetLightsOn(bool on) => throw new NotSupportedException();

    public void ToggleLocation() => throw new NotSupportedException();

    public void RetryPreferredLayer() => throw new NotSupportedException();

    public void IdentifyDisplays() => throw new NotSupportedException();
}

/// <summary>Records "Show in Folder".</summary>
internal sealed class FakeShell : IShellOperations
{
    public List<string> ShownFiles { get; } = [];

    public void ShowInFolder(string filePath) => ShownFiles.Add(filePath);

    public void OpenFolder(string path) => throw new NotSupportedException();

    public void OpenSettingsUri(string uri) => throw new NotSupportedException();

    public void OpenProjectHomePage() => throw new NotSupportedException();

    public void OpenControlPanel(string arguments) => throw new NotSupportedException();

    public bool MoveToRecycleBin(string path) => throw new NotSupportedException();

    public string? ResolveShortcut(string shortcutPath) => throw new NotSupportedException();
}
