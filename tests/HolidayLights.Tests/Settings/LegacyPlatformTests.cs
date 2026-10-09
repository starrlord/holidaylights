using System.Windows.Interop;
using HolidayLights.Core.Legacy;
using HolidayLights.Platform.Legacy;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;
using Microsoft.Win32;

namespace HolidayLights.Tests.Settings;

/// <summary>
/// The read-only 5.4 registry reader and the leftovers service, against a private key under
/// <c>HKCU\Software\HolidayLightsTests\&lt;guid&gt;</c> (deleted afterwards), a temporary Startup folder and a window of the
/// test's own. The real 5.4 key, Startup folder and a running 5.4 are never touched.
/// </summary>
public sealed class LegacyPlatformTests : IDisposable
{
    private readonly string testKey = TestRegistryKeys.NewPath();
    private readonly TempDataRoot root = new();
    private readonly string startup;
    private readonly FakeShellOperations shell = new();
    private readonly RecordingLog log = new();

    public LegacyPlatformTests() => startup = Directory.CreateDirectory(Path.Combine(root.Root, "Startup")).FullName;

    private string UserKey => testKey + @"\Holiday Lights";

    private LegacyRegistryLocations Locations => new(UserKey, null, startup);

    public void Dispose()
    {
        TestRegistryKeys.Delete(testKey);
        root.Dispose();
    }

    [Fact]
    public void Read_WithoutThe54Key_IsNull() => Assert.Null(new LegacyRegistryReader(shell, log, Locations).Read());

    [Fact]
    public void Read_ReturnsEveryValueThemeAndCategoryAsStored()
    {
        string programFolder = Directory.CreateDirectory(Path.Combine(root.Root, "Utils", "HolidayLights")).FullName;
        byte[] bulbSettings = LegacyRaw.BulbSettings(0, -1, -1, -1, -1, -1, 1, 1).Bytes!;
        using (RegistryKey main = Registry.CurrentUser.CreateSubKey(UserKey))
        {
            main.SetValue("Path", Path.Combine(programFolder, "Holiday Lights.exe"), RegistryValueKind.String);
            main.SetValue("Screen Saver Message", "Happy Holidays!", RegistryValueKind.String);
            main.SetValue("Flash Interval", 5, RegistryValueKind.DWord);
            main.SetValue("Screen Saver Message Color", unchecked((int)0xFF00FF00), RegistryValueKind.DWord);
            main.SetValue("Bulb Settings", bulbSettings, RegistryValueKind.Binary);
            using (RegistryKey theme = main.CreateSubKey(@"Themes\Christmas 1"))
            {
                theme.SetValue("Flash Pattern", 1, RegistryValueKind.DWord);
            }

            using (RegistryKey nested = main.CreateSubKey(@"Themes\Mom\Dad"))
            {
                nested.SetValue("Screen Saver Message", "Hi", RegistryValueKind.String);
            }

            main.CreateSubKey(@"Themes\Empty").Dispose();
            using RegistryKey categories = main.CreateSubKey("Included Bulb Categories");
            categories.SetValue("Standard Bulbs", "Christmas|Light Bulbs", RegistryValueKind.String);
        }

        string shortcut = Path.Combine(startup, "Holiday Lights.lnk");
        File.WriteAllText(shortcut, "shortcut");
        File.WriteAllText(Path.Combine(startup, "Other.lnk"), "shortcut");
        shell.Shortcuts[shortcut] = @"C:\PROGRA~1\HOLIDA~1\HOLIDA~1.EXE";

        LegacyRegistrySnapshot snapshot = new LegacyRegistryReader(shell, log, Locations).Read()!;

        Assert.Equal("Happy Holidays!", snapshot.Values["screen saver message"].Text);
        Assert.Equal(5u, snapshot.Values["Flash Interval"].Number);
        Assert.Equal(0xFF00FF00u, snapshot.Values["Screen Saver Message Color"].Number);
        Assert.Equal(bulbSettings, snapshot.Values["Bulb Settings"].Bytes);
        Assert.Equal(LegacyValueKind.Binary, snapshot.Values["Bulb Settings"].Kind);
        Assert.Equal(["Christmas 1", "Empty", @"Mom\Dad"], snapshot.Themes.Select(t => t.Key));
        Assert.Equal(1u, snapshot.Themes[0].Value["Flash Pattern"].Number);
        Assert.Equal("Hi", snapshot.Themes[2].Value["Screen Saver Message"].Text);
        Assert.Empty(snapshot.Themes[1].Value);
        Assert.Equal("Christmas|Light Bulbs", snapshot.IncludedBulbCategories["Standard Bulbs"]);
        Assert.Equal([new KeyValuePair<string, string?>(shortcut, @"C:\PROGRA~1\HOLIDA~1\HOLIDA~1.EXE")], snapshot.StartupShortcuts);
        Assert.Equal(LegacyNativeMethods.GetLongPath(programFolder), snapshot.ProgramFolder);
        Assert.Null(snapshot.MachinePath);
    }

    [Fact]
    public void ProgramFolder_IsTheLongFormOfAnExistingFolder()
    {
        string folder = Directory.CreateDirectory(Path.Combine(root.Root, "Holiday Lights Program")).FullName;

        Assert.Equal(LegacyNativeMethods.GetLongPath(folder), LegacyRegistryReader.ProgramFolder($"\"{Path.Combine(folder, "HOLIDA~1.EXE")}\""));
        Assert.Null(LegacyRegistryReader.ProgramFolder(Path.Combine(root.Root, "Missing", "Holiday Lights.exe")));
        Assert.Null(LegacyRegistryReader.ProgramFolder("Holiday Lights.exe"));
        Assert.Null(LegacyRegistryReader.ProgramFolder(null));
    }

    [Fact]
    public void Detect_FindsOnlyStartupShortcutsThatStartThe54Program()
    {
        using (RegistryKey main = Registry.CurrentUser.CreateSubKey(UserKey))
        {
            main.SetValue("Path", @"C:\Old Programs\HL54\Lights.exe", RegistryValueKind.String);
        }

        string mine = Path.Combine(startup, "Holiday Lights 6.lnk");
        string legacy = Path.Combine(startup, "My Holiday Lights.lnk");
        File.WriteAllText(mine, "shortcut");
        File.WriteAllText(legacy, "shortcut");
        shell.Shortcuts[mine] = @"C:\Users\Pat\AppData\Local\Programs\HolidayLights\HolidayLights.exe";
        shell.Shortcuts[legacy] = @"C:\Old Programs\HL54\Lights.exe";

        LegacyLeftoverState state = Leftovers(allowSystemChanges: true, window: 0).Detect();

        Assert.False(state.IsRunning);
        Assert.Equal(legacy, state.StartupShortcut);

        shell.Shortcuts[legacy] = @"D:\Anywhere\Holiday Lights.exe";
        Assert.Equal(legacy, Leftovers(allowSystemChanges: true, window: 0).Detect().StartupShortcut);
        shell.Shortcuts.Remove(legacy);
        Assert.Null(Leftovers(allowSystemChanges: true, window: 0).Detect().StartupShortcut);
    }

    [Fact]
    public void RemoveStartupShortcut_RecyclesIt_UnlessSystemChangesAreOff()
    {
        string legacy = Path.Combine(startup, "Holiday Lights.lnk");
        File.WriteAllText(legacy, "shortcut");
        shell.Shortcuts[legacy] = @"C:\Program Files\Holiday Lights\Holiday Lights.exe";

        Assert.False(Leftovers(allowSystemChanges: false, window: 0).RemoveStartupShortcut());
        Assert.Empty(shell.Recycled);

        Assert.True(Leftovers(allowSystemChanges: true, window: 0).RemoveStartupShortcut());
        Assert.Equal([legacy], shell.Recycled);
    }

    [Fact]
    public void CloseRunningInstance_SendsThe54ExitCommand()
    {
        StaThread.Run(() =>
        {
            var commands = new List<nint>();
            using var window = new HwndSource(new HwndSourceParameters("Holiday Lights 5.4 stand-in") { WindowStyle = 0 });
            window.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x0111)
                {
                    commands.Add(wParam);
                    handled = true;
                }

                return 0;
            });

            Assert.True(Leftovers(allowSystemChanges: true, window: window.Handle).Detect().IsRunning);
            Assert.False(Leftovers(allowSystemChanges: false, window: window.Handle).CloseRunningInstance());
            Assert.Empty(commands);

            Assert.True(Leftovers(allowSystemChanges: true, window: window.Handle).CloseRunningInstance());
            Assert.Equal([(nint)106], commands);
        });

        Assert.False(Leftovers(allowSystemChanges: true, window: 0).CloseRunningInstance());
    }

    private LegacyLeftovers Leftovers(bool allowSystemChanges, nint window) =>
        new(shell, new AppRuntimeOptions { AllowSystemChanges = allowSystemChanges }, log, Locations, () => window);
}
