namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>The reference PC (PRODUCT-SPEC 1.1): two 3840 x 2160 displays at 150 %, the second at x = -3840, 72 px taskbars.</summary>
public sealed class FakeDisplayService : IDisplayService
{
    /// <summary>Creates the displays.</summary>
    /// <param name="displays">The displays, or null for the reference PC.</param>
    public FakeDisplayService(IReadOnlyList<DisplayInfo>? displays = null) => Displays = displays ?? ReferencePc();

    /// <inheritdoc />
    public event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged;

    /// <inheritdoc />
    public IReadOnlyList<DisplayInfo> Displays { get; private set; }

    /// <inheritdoc />
    public DisplayInfo Primary => Displays.First(d => d.IsPrimary);

    /// <summary>The two displays of the reference PC.</summary>
    /// <returns>Display 1 (main) and Display 2.</returns>
    public static IReadOnlyList<DisplayInfo> ReferencePc() =>
    [
        new DisplayInfo
        {
            DeviceId = @"\\?\DISPLAY#DELA1F0#1", DeviceName = @"\\.\DISPLAY1", FriendlyName = "DELL U2723QE", Number = 1, IsPrimary = true,
            Bounds = new RectI(0, 0, 3840, 2160), WorkArea = new RectI(0, 0, 3840, 2088), Dpi = 144,
        },
        new DisplayInfo
        {
            DeviceId = @"\\?\DISPLAY#DELA1F0#2", DeviceName = @"\\.\DISPLAY2", FriendlyName = "DELL U2723QE", Number = 2,
            Bounds = new RectI(-3840, 0, 0, 2160), WorkArea = new RectI(-3840, 0, 0, 2088), Dpi = 144,
        },
    ];

    /// <summary>Replaces the displays and raises <see cref="DisplaysChanged"/>.</summary>
    /// <param name="displays">The new displays.</param>
    public void SetDisplays(IReadOnlyList<DisplayInfo> displays)
    {
        IReadOnlyList<DisplayInfo> previous = Displays;
        Displays = displays;
        DisplaysChanged?.Invoke(this, new DisplaysChangedEventArgs(previous, displays));
    }

    /// <inheritdoc />
    public DisplayInfo FromPoint(PointI point) => Displays.FirstOrDefault(d => d.Bounds.Contains(point)) ?? Primary;

    /// <inheritdoc />
    public DisplayInfo FromCursor() => Primary;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>A wallpaper provider that reports one picture (or none: the night gradient).</summary>
/// <param name="imagePath">The picture, or null.</param>
public sealed class FakeWallpaperProvider(string? imagePath = null) : IWallpaperProvider
{
    /// <inheritdoc />
    public WallpaperInfo GetWallpaper(DisplayInfo display) => new(imagePath, WallpaperPosition.Fill, imagePath is null ? null : RgbColor.Black);
}

/// <summary>Facts about Windows that a test sets before it creates the windows.</summary>
public sealed class FakeSystemInfo : ISystemInfo
{
    /// <summary>Never raised: the facts do not change while a test runs.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool AnimationsEnabled { get; set; } = true;

    /// <inheritdoc />
    public bool HighContrast { get; set; }

    /// <inheritdoc />
    public bool TaskbarUsesLightTheme { get; set; }

    /// <inheritdoc />
    public string RegionCode { get; set; } = "US";

    /// <inheritdoc />
    public string UserDisplayName { get; set; } = "Pat Smith";

    /// <inheritdoc />
    public bool IsRemoteSession { get; set; }

    /// <inheritdoc />
    public int OsBuild { get; set; } = 26300;
}

/// <summary>Shell operations that only record what they were asked to do.</summary>
public sealed class FakeShell : IShellOperations
{
    /// <summary>Every call, e.g. "OpenFolder C:\...".</summary>
    public List<string> Calls { get; } = [];

    /// <inheritdoc />
    public void OpenFolder(string path) => Calls.Add($"OpenFolder {path}");

    /// <inheritdoc />
    public void ShowInFolder(string filePath) => Calls.Add($"ShowInFolder {filePath}");

    /// <inheritdoc />
    public void OpenSettingsUri(string uri) => Calls.Add($"OpenSettingsUri {uri}");

    /// <inheritdoc />
    public void OpenProjectHomePage() => Calls.Add("OpenProjectHomePage");

    /// <inheritdoc />
    public void OpenControlPanel(string arguments) => Calls.Add($"OpenControlPanel {arguments}");

    /// <inheritdoc />
    public bool MoveToRecycleBin(string path)
    {
        Calls.Add($"MoveToRecycleBin {path}");
        return true;
    }

    /// <inheritdoc />
    public string? ResolveShortcut(string shortcutPath) => null;
}

/// <summary>A Run value that lives in memory.</summary>
public sealed class FakeStartup : IStartupRegistration
{
    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public bool IsDisabledByWindows { get; set; }

    /// <inheritdoc />
    public void Enable() => IsEnabled = true;

    /// <inheritdoc />
    public void Disable() => IsEnabled = false;
}

/// <summary>A <c>.bul</c> association that lives in memory.</summary>
public sealed class FakeFileAssociation : IFileAssociation
{
    /// <summary>The state.</summary>
    public FileAssociationState State { get; set; } = FileAssociationState.Registered;

    /// <inheritdoc />
    public FileAssociationState GetState() => State;

    /// <inheritdoc />
    public void Register() => State = FileAssociationState.Registered;

    /// <inheritdoc />
    public void Unregister() => State = FileAssociationState.NotRegistered;
}

/// <summary>A Windows screen saver configuration that lives in memory.</summary>
public sealed class FakeScreenSaverRegistration : IScreenSaverRegistration
{
    /// <summary>The status.</summary>
    public ScreenSaverStatus Status { get; set; } = new(ScreenSaverState.NotOurs, null, 600, false);

    /// <summary>Every change, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <inheritdoc />
    public ScreenSaverStatus GetStatus() => Status;

    /// <inheritdoc />
    public ScreenSaverPrevious Use()
    {
        Calls.Add("Use");
        var previous = new ScreenSaverPrevious { ScrnsaveExe = Status.ScrnsaveExe, Active = true, TimeoutSeconds = Status.TimeoutSeconds };
        Status = Status with { State = ScreenSaverState.Ours, ScrnsaveExe = @"C:\HolidayLights\Holiday Lights.scr" };
        return previous;
    }

    /// <inheritdoc />
    public void StopUsing(ScreenSaverPrevious? previous)
    {
        Calls.Add("StopUsing");
        Status = Status with { State = ScreenSaverState.NotOurs, ScrnsaveExe = previous?.ScrnsaveExe };
    }

    /// <inheritdoc />
    public void SetTimeout(TimeSpan timeout)
    {
        Calls.Add($"SetTimeout {timeout.TotalSeconds}");
        Status = Status with { TimeoutSeconds = (int)timeout.TotalSeconds };
    }

    /// <inheritdoc />
    public void TurnOn()
    {
        Calls.Add("TurnOn");
        Status = Status with { State = ScreenSaverState.Ours };
    }

    /// <inheritdoc />
    public void OpenWindowsSettings() => Calls.Add("OpenWindowsSettings");
}

/// <summary>No Holiday Lights 5.4 on this PC (or a configurable one).</summary>
public sealed class FakeLegacyImporter : ILegacyImporter
{
    /// <summary>True when 5.4 settings are present.</summary>
    public bool Present { get; set; }

    /// <inheritdoc />
    public bool IsLegacyInstallPresent() => Present;

    /// <inheritdoc />
    public LegacyImportPreview? Analyze() => Present ? new LegacyImportPreview { IsFactoryDefault = true, LegacyValues = new ThemeableSettings() } : null;

    /// <inheritdoc />
    public LegacyImportResult Import(LegacyImportPreview preview, AppSettings current, LegacyImportMode mode)
    {
        var record = new Import54Record { Date = new DateOnly(2026, 10, 8), FactoryDefaults = preview.IsFactoryDefault, Themes = 11 };
        return new LegacyImportResult(current with { Current = preview.LegacyValues, Import54 = record }, record);
    }
}

/// <summary>No 5.4 leftovers (or configurable ones).</summary>
public sealed class FakeLegacyLeftovers : ILegacyLeftovers
{
    /// <summary>The state.</summary>
    public LegacyLeftoverState State { get; set; } = new(false, null);

    /// <inheritdoc />
    public LegacyLeftoverState Detect() => State;

    /// <inheritdoc />
    public bool CloseRunningInstance()
    {
        bool was = State.IsRunning;
        State = State with { IsRunning = false };
        return was;
    }

    /// <inheritdoc />
    public bool RemoveStartupShortcut()
    {
        bool had = State.StartupShortcut is not null;
        State = State with { StartupShortcut = null };
        return had;
    }
}
