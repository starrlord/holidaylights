using System.Runtime.InteropServices;
using HolidayLights.Platform.Input;
using HolidayLights.Platform.Native;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// Registers combinations nobody uses (Ctrl+Alt+Shift+F22/F23) for a moment on a test UI thread; they are unregistered
/// when the service is disposed.
/// </summary>
public sealed partial class HotKeyServiceTests
{
    private static readonly HotKeyBinding Rare = new() { Enabled = true, Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift, Key = "F23" };
    private static readonly HotKeyBinding OtherRare = Rare with { Key = "F22" };

    [Fact]
    public void Register_TakesTheCombinationUntilUnregistered()
    {
        using var ui = new DispatcherThread();
        HotKeyService service = ui.Invoke(() => new HotKeyService(new RecordingLog()));
        try
        {
            Assert.Equal(HotKeyRegistration.Registered, ui.Invoke(() => service.Register(HotKeyAction.SwitchLocation, Rare)));

            // Its own registration is not "in use"; the other hot key cannot have the same combination.
            Assert.Equal(HotKeyValidity.Ok, ui.Invoke(() => service.Validate(Rare, HotKeyAction.SwitchLocation)).Validity);
            Assert.Equal(HotKeyValidity.InUse, ui.Invoke(() => service.Validate(Rare, HotKeyAction.ToggleLights)).Validity);
            Assert.Equal(HotKeyRegistration.InUse, ui.Invoke(() => service.Register(HotKeyAction.ToggleLights, Rare)));

            ui.Invoke(() => service.Unregister(HotKeyAction.SwitchLocation));
            Assert.Equal(HotKeyValidity.Ok, ui.Invoke(() => service.Validate(Rare, HotKeyAction.ToggleLights)).Validity);
        }
        finally
        {
            ui.Invoke(service.Dispose);
        }
    }

    [Fact]
    public void Register_ReplacesTheActionsPreviousCombination()
    {
        using var ui = new DispatcherThread();
        HotKeyService service = ui.Invoke(() => new HotKeyService(new RecordingLog()));
        try
        {
            Assert.Equal(HotKeyRegistration.Registered, ui.Invoke(() => service.Register(HotKeyAction.ToggleLights, Rare)));
            Assert.Equal(HotKeyRegistration.Registered, ui.Invoke(() => service.Register(HotKeyAction.ToggleLights, OtherRare)));

            // The first combination was released.
            Assert.Equal(HotKeyValidity.Ok, ui.Invoke(() => service.Validate(Rare, HotKeyAction.SwitchLocation)).Validity);
        }
        finally
        {
            ui.Invoke(service.Dispose);
        }
    }

    [Theory]
    [InlineData(HotKeyModifiers.Shift, "A")]
    [InlineData(HotKeyModifiers.None, "B")]
    [InlineData(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "Escape")]
    public void Register_RefusesUnsafeCombinations(HotKeyModifiers modifiers, string key)
    {
        using var ui = new DispatcherThread();
        var log = new RecordingLog();
        HotKeyService service = ui.Invoke(() => new HotKeyService(log));
        try
        {
            Assert.Equal(HotKeyRegistration.Unsafe, ui.Invoke(() => service.Register(HotKeyAction.ToggleLights, new HotKeyBinding { Modifiers = modifiers, Key = key })));
            Assert.Contains(log.Entries, e => e.Level == AppLogLevel.Warning);
        }
        finally
        {
            ui.Invoke(service.Dispose);
        }
    }

    [Fact]
    public void Pressed_IsRaisedOnTheUiThreadForRegisteredHotKeysOnly()
    {
        using var ui = new DispatcherThread();
        HotKeyService service = ui.Invoke(() => new HotKeyService(new RecordingLog()));
        try
        {
            var pressed = new List<(HotKeyAction Action, int Thread)>();
            int uiThread = ui.Invoke(() => Environment.CurrentManagedThreadId);
            service.Pressed += (_, e) => pressed.Add((e.Action, Environment.CurrentManagedThreadId));
            Assert.Equal(HotKeyRegistration.Registered, ui.Invoke(() => service.Register(HotKeyAction.SwitchLocation, Rare)));

            nint window = FindOwnWindow("Holiday Lights Hot Keys");
            NativeMethods.PostMessage(window, NativeMethods.WM_HOTKEY, (nint)(HotKeyAction.ToggleLights + 1), 0);
            NativeMethods.PostMessage(window, NativeMethods.WM_HOTKEY, (nint)(HotKeyAction.SwitchLocation + 1), 0);

            Assert.True(DispatcherThread.WaitFor(() => pressed.Count > 0, TimeSpan.FromSeconds(5)));
            ui.Invoke(() => 0);
            (HotKeyAction action, int thread) = Assert.Single(pressed);
            Assert.Equal(HotKeyAction.SwitchLocation, action);
            Assert.Equal(uiThread, thread);
        }
        finally
        {
            ui.Invoke(service.Dispose);
        }
    }

    [Fact]
    public void Register_FromAnotherThreadIsRefused()
    {
        using var ui = new DispatcherThread();
        HotKeyService service = ui.Invoke(() => new HotKeyService(new RecordingLog()));
        try
        {
            Assert.Throws<InvalidOperationException>(() => service.Register(HotKeyAction.ToggleLights, Rare));
            Assert.Throws<InvalidOperationException>(() => service.Validate(Rare, HotKeyAction.ToggleLights));
        }
        finally
        {
            ui.Invoke(service.Dispose);
        }
    }

    [Fact]
    public void SettingChanges_WithTheSameLayoutsRaiseNothing()
    {
        using var ui = new DispatcherThread();
        HotKeyService service = ui.Invoke(() => new HotKeyService(new RecordingLog()));
        try
        {
            int raised = 0;
            service.KeyboardLayoutsChanged += (_, _) => raised++;
            NativeMethods.PostMessage(FindOwnWindow("Holiday Lights Hot Keys"), NativeMethods.WM_SETTINGCHANGE, 0, 0);
            Thread.Sleep(200);
            ui.Invoke(() => 0);

            Assert.Equal(0, raised);
        }
        finally
        {
            ui.Invoke(service.Dispose);
        }
    }

    [Fact]
    public void KeyboardLayouts_AreListedAndNamed()
    {
        IReadOnlyList<nint> layouts = KeyboardLayouts.Installed();

        Assert.NotEmpty(layouts);
        Assert.Equal(layouts.Order(), layouts);
        foreach (nint layout in layouts)
        {
            Assert.False(string.IsNullOrWhiteSpace(KeyboardLayouts.DisplayName(layout)));
        }
    }

    [Theory]
    [InlineData(0x04090409L, "00000409")]
    [InlineData(0x04150415L, "00000415")]
    [InlineData(0x08090409L, "00000809")]
    [InlineData(0xE0010411L, "E0010411")]
    [InlineData(0xF0020409L, "00010409")]
    public void LayoutId_FollowsTheHklRules(long hkl, string expected) =>
        Assert.Equal(expected, KeyboardLayouts.LayoutId((nint)hkl));

    [Fact]
    public void DisplayName_ReadsTheLocalizedLayoutName()
    {
        Assert.Equal("Polish (Programmers)", KeyboardLayouts.DisplayName(0x04150415));
        Assert.Equal("US", KeyboardLayouts.DisplayName(0x04090409));
    }

    [Fact]
    public void TheUsLayout_TypesNothingWithCtrlAlt()
    {
        nint us = KeyboardLayouts.Installed().FirstOrDefault(l => KeyboardLayouts.LayoutId(l) == "00000409");
        if (us == 0)
        {
            return;
        }

        Assert.Null(KeyboardLayouts.TypedText(us, 'Q', shift: false));
        Assert.Null(KeyboardLayouts.TypedText(us, 'B', shift: true));
    }

    internal static nint FindOwnWindow(string title)
    {
        for (nint window = FindWindowEx(0, 0, "HolidayLights.Platform.MessageWindow", title);
             window != 0;
             window = FindWindowEx(0, window, "HolidayLights.Platform.MessageWindow", title))
        {
            NativeMethods.GetWindowThreadProcessId(window, out uint process);
            if (process == (uint)Environment.ProcessId)
            {
                return window;
            }
        }

        throw new InvalidOperationException($"The {title} window was not found.");
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);
}
