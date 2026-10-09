using HolidayLights.Platform.Displays;
using HolidayLights.Platform.Native;
using HolidayLights.Platform.Shell;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>Read-only probes of this machine: facts about Windows and the wallpaper.</summary>
public sealed class SystemProbeTests
{
    [Fact]
    public void SystemInfo_ReadsTheWindowsFacts()
    {
        using var ui = new DispatcherThread();
        SystemInfo info = ui.Invoke(() => new SystemInfo(new RecordingLog()));
        try
        {
            Assert.Matches("^[A-Z]{2}$", info.RegionCode);
            Assert.True(info.OsBuild >= 22621);
            Assert.False(string.IsNullOrWhiteSpace(info.UserDisplayName));
            _ = info.AnimationsEnabled;
            _ = info.HighContrast;
            _ = info.TaskbarUsesLightTheme;
            _ = info.IsRemoteSession;
        }
        finally
        {
            ui.Invoke(info.Dispose);
        }
    }

    [Fact]
    public void SystemInfo_RaisesNothingWhenASettingChangeChangesNothing()
    {
        using var ui = new DispatcherThread();
        SystemInfo info = ui.Invoke(() => new SystemInfo(new RecordingLog()));
        try
        {
            int raised = 0;
            info.Changed += (_, _) => raised++;
            NativeMethods.PostMessage(HotKeyServiceTests.FindOwnWindow("Holiday Lights System Settings"), NativeMethods.WM_SETTINGCHANGE, 0, 0);
            Thread.Sleep(200);
            ui.Invoke(() => 0);

            Assert.Equal(0, raised);
        }
        finally
        {
            ui.Invoke(info.Dispose);
        }
    }

    [Fact]
    public void RegionCode_IsATwoLetterCountry() => Assert.Matches("^[A-Z]{2}$", SystemInfo.ReadRegionCode());

    [Fact]
    [Trait("Category", "Desktop")]
    public async Task Wallpaper_IsReadForEveryDisplayWithoutThrowing()
    {
        using var ui = new DispatcherThread();
        DisplayService displays = ui.Invoke(() => new DisplayService(new RecordingLog()));
        try
        {
            var provider = new WallpaperProvider(new RecordingLog());
            foreach (DisplayInfo display in displays.Displays)
            {
                // From the UI thread (STA, inline) and from the thread pool (a helper STA thread).
                WallpaperInfo fromUi = ui.Invoke(() => provider.GetWallpaper(display));
                WallpaperInfo fromPool = await Task.Run(() => provider.GetWallpaper(display));

                Assert.Equal(fromUi, fromPool);
                Assert.True(Enum.IsDefined(fromUi.Position));
                Assert.True(fromUi.ImagePath is null || File.Exists(fromUi.ImagePath));
            }
        }
        finally
        {
            ui.Invoke(displays.Dispose);
        }
    }
}
