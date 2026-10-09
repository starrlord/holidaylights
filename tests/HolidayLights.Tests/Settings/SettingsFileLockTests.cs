using System.Security.AccessControl;
using System.Security.Principal;
using HolidayLights.Core.Settings;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

/// <summary>
/// A settings file that cannot be opened (PRODUCT-SPEC 5.13, 3.12): only content that cannot be parsed counts as damaged.
/// A file that a sync, backup or antivirus tool holds, or that cannot be accessed, is retried, never renamed and never
/// replaced before it has been read, and it is used as soon as it can be read.
/// </summary>
public sealed class SettingsFileLockTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 10, 8, 9, 0, 0);
    private static readonly TimeSpan ShortPatience = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan QuickRecovery = TimeSpan.FromMilliseconds(100);

    private readonly TempDataRoot root = new();
    private readonly RecordingLog log = new();

    public void Dispose() => root.Dispose();

    [Fact]
    public async Task Load_AFileHeldWithoutSharingForLessThanThePatience_IsRead()
    {
        WriteUserSettings();
        FileStream held = Hold(FileShare.None);
        Task release = Task.Run(async () =>
        {
            await Task.Delay(600);
            await held.DisposeAsync();
        });

        using JsonSettingsStore store = Load(JsonSettingsStore.ReadPatience);
        await release;

        Assert.Equal(SettingsLoadOutcome.Loaded, store.LoadOutcome);
        Assert.False(store.IsFileUnread);
        Assert.Equal(35, store.Current.Music.Volume);
    }

    [Fact]
    public async Task Load_AFileHeldLonger_IsNeitherRenamedNorReplaced_AndIsUsedOnceItCanBeRead()
    {
        WriteUserSettings();
        var info = new FileInfo(root.Paths.SettingsFile);
        (long Length, DateTime Written) before = (info.Length, info.LastWriteTimeUtc);
        SettingsChangedEventArgs? adoption = null;
        JsonSettingsStore store;

        // A lock that allows delete: the old code renamed the user's file and wrote the defaults in its place.
        using (Hold(FileShare.Delete))
        {
            store = Load(ShortPatience);

            Assert.Equal(SettingsLoadOutcome.Unavailable, store.LoadOutcome);
            Assert.True(store.IsFileUnread);
            Assert.Equal(new AppSettings().Music.Volume, store.Current.Music.Volume);

            store.Update(s => s with { Themes = s.Themes with { LastName = "Bookkeeping" } }, SettingsChange.Internal);
            store.Update(s => s with { Music = s.Music with { Volume = 20 } }, SettingsChange.Edit("Change the volume"));
            await store.FlushAsync();

            var after = new FileInfo(root.Paths.SettingsFile);
            Assert.True(after.Exists);
            Assert.Equal(before, (after.Length, after.LastWriteTimeUtc));
            Assert.Empty(Directory.GetFiles(root.Paths.RoamingRoot, "settings.damaged-*"));
            store.Changed += (_, e) => adoption ??= e;
        }

        using (store)
        {
            await WaitFor(() => !store.IsFileUnread && adoption is not null);

            // The user's settings, with the edit made meanwhile applied again; bookkeeping is not.
            AppSettings s = store.Current;
            Assert.False(s.Lights.On);
            Assert.Equal(["addon:Arrow"], s.Bulbs.Favorites);
            Assert.Equal("Grandma's Lights", s.Themes.LastName);
            Assert.Equal(20, s.Music.Volume);
            Assert.Equal(SettingsChangeKind.Internal, adoption!.Change.Kind);
            Assert.Same(s, adoption.NewSettings);

            await store.FlushAsync();
            AppSettings saved = HolidayLightsJson.DeserializeSettings(File.ReadAllText(root.Paths.SettingsFile));
            Assert.Equal((false, 20, "Grandma's Lights"), (saved.Lights.On, saved.Music.Volume, saved.Themes.LastName));
            Assert.Empty(Directory.GetFiles(root.Paths.RoamingRoot, "settings.damaged-*"));
        }
    }

    [Fact]
    public void Exit_WhileTheFileStillCannotBeRead_LeavesItUnchanged()
    {
        byte[] original = WriteUserSettings();
        using (Hold(FileShare.None))
        {
            JsonSettingsStore store = Load(ShortPatience);
            store.Update(s => s with { Music = s.Music with { Volume = 20 } }, SettingsChange.Edit("Change the volume"));

            store.Dispose();
        }

        Assert.Equal(original, File.ReadAllBytes(root.Paths.SettingsFile));
        Assert.Empty(Directory.GetFiles(root.Paths.RoamingRoot, "settings.damaged-*"));
        Assert.Contains(log.Entries, e => e.Message.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_AFileThatCannotBeAccessed_IsRetriedAndLeftAlone_AndUsedOnceAccessReturns()
    {
        WriteUserSettings();
        var file = new FileInfo(root.Paths.SettingsFile);
        FileSecurity security = file.GetAccessControl();
        var deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
        security.AddAccessRule(deny);
        file.SetAccessControl(security);
        JsonSettingsStore store;
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(file.FullName));
            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            store = Load(ShortPatience);

            Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) >= ShortPatience, "UnauthorizedAccessException is retried.");
            Assert.Equal(SettingsLoadOutcome.Unavailable, store.LoadOutcome);
            Assert.True(store.IsFileUnread);
            Assert.True(File.Exists(file.FullName));
            Assert.Empty(Directory.GetFiles(root.Paths.RoamingRoot, "settings.damaged-*"));
        }
        finally
        {
            security.RemoveAccessRule(deny);
            file.SetAccessControl(security);
        }

        using (store)
        {
            await WaitFor(() => !store.IsFileUnread);
            Assert.Equal(35, store.Current.Music.Volume);
        }
    }

    [Fact]
    public async Task Load_ADamagedFileThatCannotBeRenamed_IsCopiedBeforeItIsReplaced()
    {
        Directory.CreateDirectory(root.Paths.RoamingRoot);
        byte[] damaged = "{ \"music\": { \"volume\": 35 "u8.ToArray();
        File.WriteAllBytes(root.Paths.SettingsFile, damaged);
        JsonSettingsStore store;

        // Another program reads the file without sharing delete: it can be read, but not renamed.
        using (Hold(FileShare.Read))
        {
            store = Load(ShortPatience);
        }

        using (store)
        {
            Assert.Equal(SettingsLoadOutcome.ReplacedDamaged, store.LoadOutcome);
            Assert.False(store.IsFileUnread);
            Assert.Equal(damaged, File.ReadAllBytes(Path.Combine(root.Paths.RoamingRoot, "settings.damaged-2026-10-08.json")));

            await store.FlushAsync();

            Assert.Equal(new AppSettings().Music.Volume, HolidayLightsJson.DeserializeSettings(File.ReadAllText(root.Paths.SettingsFile)).Music.Volume);
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(20);
        }
    }

    /// <summary>Loads with the given patience; settings read later are adopted on a thread-pool context (the UI thread in the app).</summary>
    private JsonSettingsStore Load(TimeSpan patience)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try
        {
            return JsonSettingsStore.Load(root.Paths, log, SettingsMigrator.Default, Today, patience, QuickRecovery);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Another program's handle on the settings file, sharing only <paramref name="share"/>.</summary>
    private FileStream Hold(FileShare share) => new(root.Paths.SettingsFile, FileMode.Open, FileAccess.Read, share);

    /// <summary>A user's settings that differ from the defaults.</summary>
    private byte[] WriteUserSettings()
    {
        var settings = new AppSettings
        {
            Lights = new LightsSettings { On = false },
            Music = new MusicSettings { Volume = 35 },
            Bulbs = new BulbPreferences { Favorites = ["addon:Arrow"] },
            Themes = new ThemePreferences { LastName = "Grandma's Lights" },
            Onboarding = new OnboardingState { WelcomeShown = true },
        };
        Directory.CreateDirectory(root.Paths.RoamingRoot);
        File.WriteAllText(root.Paths.SettingsFile, HolidayLightsJson.Serialize(settings));
        return File.ReadAllBytes(root.Paths.SettingsFile);
    }
}
