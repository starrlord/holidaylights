using HolidayLights.Audio;
using HolidayLights.Audio.Events;
using HolidayLights.Core.Flash;
using HolidayLights.Core.Layout;
using HolidayLights.Core.Sprites;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>A music engine that records what the saver asks of it.</summary>
internal sealed class FakeDirector : IMusicDirector
{
    public List<MusicPolicy> Policies { get; } = [];

    public TimeSpan? StoppedWith { get; private set; }

    public MusicState State => MusicState.Initial;

    public IMusicEventSource Events { get; } = new MusicEventHub();

    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public void ApplyPolicy(MusicPolicy policy) => Policies.Add(policy);

    public Task StopAsync(TimeSpan fadeOut)
    {
        StoppedWith = fadeOut;
        return Task.CompletedTask;
    }

    public void PlayNow(string songId) => throw new NotSupportedException();

    public void NextSong() => throw new NotSupportedException();

    public void Previous() => throw new NotSupportedException();

    public void Pause() => throw new NotSupportedException();

    public void Resume() => throw new NotSupportedException();

    public void Seek(TimeSpan position) => throw new NotSupportedException();

    public void RetryNow() => throw new NotSupportedException();

    public IReadOnlyList<string> GetMidiOutputDevices() => [];

    public void Dispose()
    {
    }
}

/// <summary>Counts screen saver sessions.</summary>
internal sealed class RecordingSessions : IScreenSaverSessions
{
    public int Started { get; private set; }

    public int Stopped { get; private set; }

    public bool IsSaverRunning => Started > Stopped;

    public void SaverStarted() => Started++;

    public void SaverStopped() => Stopped++;
}

/// <summary>A local, non-remote session.</summary>
internal sealed class FakeSystemInfo : ISystemInfo
{
    public bool AnimationsEnabled => true;

    public bool HighContrast => false;

    public bool TaskbarUsesLightTheme => false;

    public string RegionCode => "US";

    public string UserDisplayName => "Test";

    public bool IsRemoteSession => false;

    public int OsBuild => 26300;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>A fixed set of displays (possibly none, as while every display is off); the main display defaults to 1920 x 1080.</summary>
internal sealed class FakeDisplays(params DisplayInfo[] displays) : IDisplayService
{
    public IReadOnlyList<DisplayInfo> Displays => displays;

    public DisplayInfo Primary { get; } = displays.FirstOrDefault() ?? SaverTestKit.Display(1920, 1080);

    public event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged
    {
        add { }
        remove { }
    }

    public DisplayInfo FromPoint(PointI point) => Primary;

    public DisplayInfo FromCursor() => Primary;

    public void Dispose()
    {
    }
}

/// <summary>
/// The application services as a saver process sees them: the real engines and picture library of a
/// <see cref="SaverTestKit"/>, the given displays, a recording music engine and session tracker. Every other service
/// throws: the screen saver must never use it (in particular nothing that changes Windows settings).
/// </summary>
internal sealed class SaverAppServices(SaverTestKit kit, IDisplayService displays, FakeDirector music, RecordingSessions sessions) : IAppServices
{
    public DataPaths Paths => kit.Root.Paths;

    public AppRuntimeOptions Options { get; } = new() { AllowSystemChanges = false, Session = AppSessionKind.ScreenSaver };

    public IAppLog Log => kit.Log;

    public ISettingsStore Settings => kit.Settings;

    public IBulbCatalog Bulbs => kit.Catalog;

    public ILayoutEngine Layout { get; } = new ClassicLayoutEngine();

    public IFlashEngine Flash { get; } = new FlashEngine();

    public ISpriteProvider Sprites => kit.Sprites;

    public ICpuCompositor Compositor { get; } = new CpuCompositor();

    public IMusicDirector Music => music;

    public IPictureLibrary Pictures => kit.Pictures;

    public IDisplayService Displays => displays;

    public ISystemInfo SystemInfo { get; } = new FakeSystemInfo();

    public IScreenSaverSessions SaverSessions => sessions;

    public IThemeLibrary Themes => throw Unused();

    public IThemeService ThemeService => throw Unused();

    public ISeasonCalendar Calendar => throw Unused();

    public ILegacyImporter LegacyImporter => throw Unused();

    public ILegacyLeftovers LegacyLeftovers => throw Unused();

    public ISongLibrary Songs => throw Unused();

    public IWallpaperProvider Wallpapers => throw Unused();

    public IShellOperations Shell => throw Unused();

    public IHoldingFolder Holding => throw Unused();

    public IStartupRegistration Startup => throw Unused();

    public IFileAssociation FileAssociation => throw Unused();

    public IScreenSaverRegistration ScreenSaverRegistration => throw Unused();

    public ILightsController Lights => throw Unused();

    public IHotKeyController HotKeys => throw Unused();

    public INotificationService Notifications => throw Unused();

    public IAppShell AppShell => throw Unused();

    public ISettingsWindowService SettingsWindow => throw Unused();

    public IUndoHistory Undo => throw Unused();

    public IBulbFactoryDialogs BulbFactory => throw Unused();

    public IScreenSaverService ScreenSaver => throw Unused();

    private static NotSupportedException Unused([System.Runtime.CompilerServices.CallerMemberName] string service = "") =>
        new($"The screen saver must not use {service}.");
}
