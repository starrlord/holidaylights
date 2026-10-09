using HolidayLights.Audio;

namespace HolidayLights.Tests.App.Fakes;

/// <summary>
/// An <see cref="IAppServices"/> whose members are set by each test; members a test does not set are null and fail
/// loudly when the code under test touches them.
/// </summary>
internal sealed class FakeServices : IAppServices
{
    public DataPaths Paths { get; set; } = null!;

    public AppRuntimeOptions Options { get; set; } = new();

    public IAppLog Log { get; set; } = NullAppLog.Instance;

    public ISettingsStore Settings { get; set; } = null!;

    public IThemeLibrary Themes { get; set; } = null!;

    public IThemeService ThemeService { get; set; } = null!;

    public ISeasonCalendar Calendar { get; set; } = null!;

    public ILegacyImporter LegacyImporter { get; set; } = null!;

    public ILegacyLeftovers LegacyLeftovers { get; set; } = null!;

    public IBulbCatalog Bulbs { get; set; } = null!;

    public ILayoutEngine Layout { get; set; } = null!;

    public IFlashEngine Flash { get; set; } = null!;

    public ISpriteProvider Sprites { get; set; } = null!;

    public ICpuCompositor Compositor { get; set; } = null!;

    public ISongLibrary Songs { get; set; } = null!;

    public IMusicDirector Music { get; set; } = null!;

    public IPictureLibrary Pictures { get; set; } = null!;

    public IDisplayService Displays { get; set; } = null!;

    public IWallpaperProvider Wallpapers { get; set; } = null!;

    public ISystemInfo SystemInfo { get; set; } = new FakeSystemInfo();

    public IShellOperations Shell { get; set; } = null!;

    public IHoldingFolder Holding { get; set; } = null!;

    public IStartupRegistration Startup { get; set; } = null!;

    public IFileAssociation FileAssociation { get; set; } = null!;

    public IScreenSaverRegistration ScreenSaverRegistration { get; set; } = null!;

    public ILightsController Lights { get; set; } = null!;

    public IHotKeyController HotKeys { get; set; } = null!;

    public INotificationService Notifications { get; set; } = new FakeNotifications();

    public IAppShell AppShell { get; set; } = new FakeAppShell();

    public IScreenSaverSessions SaverSessions { get; set; } = null!;

    public ISettingsWindowService SettingsWindow { get; set; } = new FakeSettingsWindow();

    public IUndoHistory Undo => SettingsWindow.Undo;

    public IBulbFactoryDialogs BulbFactory { get; set; } = null!;

    public IScreenSaverService ScreenSaver { get; set; } = null!;
}

/// <summary>Records notifications instead of showing them.</summary>
internal sealed class FakeNotifications : INotificationService
{
    public List<(NotificationKind Kind, string Title, string Text)> Shown { get; } = [];

    public bool Show(NotificationKind kind, string title, string text)
    {
        Shown.Add((kind, title, text));
        return true;
    }
}

/// <summary>Records the shell commands.</summary>
internal sealed class FakeAppShell : IAppShell
{
    public List<string?> HelpTopics { get; } = [];

    public int AboutShown { get; private set; }

    public int Exits { get; private set; }

    public void ShowAbout() => AboutShown++;

    public void ShowHelp(string? topicId = null) => HelpTopics.Add(topicId);

    public Task ExitAsync()
    {
        Exits++;
        return Task.CompletedTask;
    }

    public void StartUninstall()
    {
    }
}

/// <summary>A Settings window that records what it was asked to show.</summary>
internal sealed class FakeSettingsWindow : ISettingsWindowService
{
    public event EventHandler? Opened;

    public event EventHandler? Closed;

    public bool IsOpen { get; set; }

    public System.Windows.Window? Window => null;

    public SettingsPageId? CurrentPage { get; set; }

    public IUndoHistory Undo { get; } = new NoUndo();

    public List<(SettingsPageId? Page, SettingsRequest? Request)> Shown { get; } = [];

    public void Show(SettingsPageId? page = null, SettingsRequest? request = null)
    {
        Shown.Add((page, request));
        IsOpen = true;
        CurrentPage = page ?? CurrentPage ?? SettingsPageId.Home;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void CloseKeepingChanges() => Close();

    public void Close()
    {
        IsOpen = false;
        CurrentPage = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NoUndo : IUndoHistory
    {
        public bool IsRecording => false;

        public void Record(UndoStep step)
        {
        }

        public IDisposable BeginGroup(string description) => new Group();

        private sealed class Group : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}

/// <summary>Facts about a Windows that tests choose.</summary>
internal sealed class FakeSystemInfo : ISystemInfo
{
    public event EventHandler? Changed;

    public bool AnimationsEnabled { get; set; } = true;

    public bool HighContrast { get; set; }

    public bool TaskbarUsesLightTheme { get; set; }

    public string RegionCode { get; set; } = "US";

    public string UserDisplayName { get; set; } = "Test User";

    public bool IsRemoteSession { get; set; }

    public int OsBuild { get; set; } = 26300;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Displays that tests choose (the reference PC by default: two 3840 x 2160 at 150 %, the second at x = -3840).</summary>
internal sealed class FakeDisplayService : IDisplayService
{
    public event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged;

    public IReadOnlyList<DisplayInfo> Displays { get; private set; } = ReferencePc();

    public DisplayInfo Primary => Displays.First(d => d.IsPrimary);

    public static DisplayInfo[] ReferencePc() => [Display(1, 0, primary: true), Display(2, -3840, primary: false)];

    public static DisplayInfo Display(int number, int left, bool primary, int dpi = 144, int width = 3840, int height = 2160, int taskbar = 72) => new()
    {
        DeviceId = $"display-{number}",
        DeviceName = $@"\\.\DISPLAY{number}",
        Number = number,
        Bounds = RectI.FromXYWH(left, 0, width, height),
        WorkArea = RectI.FromXYWH(left, 0, width, height - taskbar),
        Dpi = dpi,
        IsPrimary = primary,
    };

    public void Change(IReadOnlyList<DisplayInfo> displays)
    {
        IReadOnlyList<DisplayInfo> previous = Displays;
        Displays = displays;
        DisplaysChanged?.Invoke(this, new DisplaysChangedEventArgs(previous, displays));
    }

    public DisplayInfo FromPoint(PointI point) => Displays.FirstOrDefault(d => d.Bounds.Contains(point)) ?? Primary;

    public DisplayInfo FromCursor() => Primary;

    public void Dispose()
    {
    }
}

/// <summary>A hot key service whose registrations succeed unless a test says otherwise.</summary>
internal sealed class FakeHotKeyService : IHotKeyService
{
    public event EventHandler<HotKeyPressedEventArgs>? Pressed;

    public event EventHandler? KeyboardLayoutsChanged;

    public Dictionary<HotKeyAction, HotKeyRegistration> Outcomes { get; } = [];

    public Dictionary<HotKeyAction, HotKeyBinding> Registered { get; } = [];

    public bool Disposed { get; private set; }

    public HotKeyRegistration Register(HotKeyAction action, HotKeyBinding binding)
    {
        HotKeyRegistration outcome = Outcomes.GetValueOrDefault(action, HotKeyRegistration.Registered);
        if (outcome == HotKeyRegistration.Registered)
        {
            Registered[action] = binding;
        }
        else
        {
            Registered.Remove(action);
        }

        return outcome;
    }

    public void Unregister(HotKeyAction action) => Registered.Remove(action);

    public HotKeyCheck Validate(HotKeyBinding binding, HotKeyAction action) => new(HotKeyValidity.Ok);

    public void Press(HotKeyAction action) => Pressed?.Invoke(this, new HotKeyPressedEventArgs(action));

    public void ChangeLayouts() => KeyboardLayoutsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}

/// <summary>Resolves the 49 built-in bulbs (the shipped classic themes use only those).</summary>
internal sealed class BuiltInResolver : IBulbResolver
{
    private static readonly Lazy<IReadOnlyDictionary<string, IBulb>> Bulbs =
        new(() => HolidayLights.Core.Bulbs.BuiltInBulbs.Load().ToDictionary(b => b.Id, BulbIds.Comparer));

    public bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb) =>
        Bulbs.Value.TryGetValue(id, out bulb);
}

/// <summary>The 46 bundled songs of the test output (theme loading converts checked and unchecked songs with them).</summary>
internal sealed class BundledSongs : ISongLibrary
{
    private readonly SongInfo[] songs = [.. Directory.EnumerateFiles(Path.Combine(Shared.TestPaths.ContentFolder, "Music"), "*.mid")
        .Select(file => new SongInfo
        {
            Id = MediaIds.Bundled(Path.GetFileName(file)),
            Title = Path.GetFileNameWithoutExtension(file),
            FilePath = file,
            Origin = MediaOrigin.Bundled,
            Kind = SongKind.Midi,
            SortKey = Path.GetFileName(file).ToUpperInvariant(),
        })
        .OrderBy(s => s.SortKey, StringComparer.Ordinal)];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<SongInfo> Songs => songs;

    public IReadOnlyList<SongInfo> HiddenSongs => [];

    public void Start()
    {
    }

    public bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? song)
    {
        song = songs.FirstOrDefault(s => MediaIds.Comparer.Equals(s.Id, id));
        return song is not null;
    }

    public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) => throw new NotSupportedException();

    public HeldItem? Remove(string id) => throw new NotSupportedException();

    public void Restore(HeldItem item) => throw new NotSupportedException();

    public void RestoreHiddenSongs() => throw new NotSupportedException();
}

/// <summary>A music engine that records the policies it receives.</summary>
internal sealed class FakeMusicDirector : IMusicDirector
{
    public event EventHandler? StateChanged;

    public MusicState State { get; set; } = MusicState.Initial;

    public IMusicEventSource Events => throw new NotSupportedException();

    public List<MusicPolicy> Policies { get; } = [];

    public int NextSongs { get; private set; }

    public void ApplyPolicy(MusicPolicy policy) => Policies.Add(policy);

    public void PlayNow(string songId)
    {
    }

    public void NextSong() => NextSongs++;

    public void Previous()
    {
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Seek(TimeSpan position)
    {
    }

    public void RetryNow()
    {
    }

    public IReadOnlyList<string> GetMidiOutputDevices() => ["Microsoft GS Wavetable Synth"];

    public Task StopAsync(TimeSpan fadeOut) => Task.CompletedTask;

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

/// <summary>A clock that tests move by hand.</summary>
internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test", Now.Offset, "Test", "Test");
}
