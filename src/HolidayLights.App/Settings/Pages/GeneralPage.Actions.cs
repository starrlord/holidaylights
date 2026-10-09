using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.App.Settings.Undo;
using HolidayLights.Core.Settings;

namespace HolidayLights.App.Settings.Pages;

/// <summary>The commands of the General page.</summary>
/// <remarks>
/// The check boxes and radios call their handlers on Click, Checked and Unchecked: UI Automation's Toggle and Select
/// (Narrator, Voice Access) and the + and - keys change IsChecked without a Click. <see cref="Update"/> applies only a real
/// change, so a click's second event and the page's own refresh change nothing.
/// </remarks>
public partial class GeneralPage
{
    private bool importing;

    private void Update(Func<AppSettings, AppSettings> transform, string description)
    {
        if (!updating)
        {
            Services.Settings.Update(
                s =>
                {
                    AppSettings changed = transform(s);
                    return changed.Equals(s) ? s : changed;
                },
                SettingsChange.Edit(description));
        }
    }

    private void OnLocationClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, OnTopCard))
        {
            Update(s => s with { Lights = s.Lights with { Drawing = BulbDrawing.OnTop } }, "Draw the bulbs on top of all windows");
        }
        else
        {
            bool behind = ReferenceEquals(sender, BehindIconsCard);
            Update(s => s with { Lights = s.Lights with { Drawing = BulbDrawing.Desktop, BehindIcons = behind } },
                behind ? "Draw the bulbs behind the desktop icons" : "Draw the bulbs in front of the desktop icons");
        }
    }

    private void OnTryAgainClick(object sender, RoutedEventArgs e) => Services.Lights.RetryPreferredLayer();

    /// <summary>Per-display "Show Lights"; the last checked display cannot be unchecked.</summary>
    private void SetDisplayEnabled(string deviceId, int number, bool enabled) => Update(
        s =>
        {
            if (s.Lights.Displays.Disabled.Contains(deviceId, StringComparer.OrdinalIgnoreCase) != enabled)
            {
                return s;
            }

            var disabled = s.Lights.Displays.Disabled.Where(id => !string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!enabled)
            {
                disabled.Add(deviceId);
            }

            bool anyOn = Services.Displays.Displays.Any(d => !disabled.Contains(d.DeviceId, StringComparer.OrdinalIgnoreCase));
            return anyOn ? s with { Lights = s.Lights with { Displays = new DisplaySelection { Disabled = disabled } } } : s;
        },
        string.Create(CultureInfo.CurrentCulture, $"{(enabled ? "Show" : "Hide")} the lights on display {number}"));

    private void OnIdentifyClick(object sender, RoutedEventArgs e) => Services.Lights.IdentifyDisplays();

    /// <summary>"Main Display Only (Like 2003)": unchecks every display but the main one (one undo step).</summary>
    private void OnMainOnlyClick(object sender, RoutedEventArgs e)
    {
        string[] others = [.. Services.Displays.Displays.Where(d => !d.IsPrimary).Select(d => d.DeviceId)];
        Update(s => s with { Lights = s.Lights with { Displays = new DisplaySelection { Disabled = others } } }, "Show the lights on the main display only");
    }

    private void OnFrameModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: FrameMode mode })
        {
            Update(s => s with { Lights = s.Lights with { FrameMode = mode } },
                mode == FrameMode.EachDisplay ? "Frame each display" : "Frame all displays together");
        }
    }

    private void OnLookClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: LookPreset preset } && LookPresets.ValuesOf(preset) is { } look)
        {
            string name = preset switch
            {
                LookPreset.BrightGlow => "Bright Glow",
                LookPreset.Classic2003 => "Classic 2003",
                _ => "Modern Glow",
            };
            Update(s => s with { Look = look }, $"Use the {name} look");
        }
        else if (!updating)
        {
            // "Custom" is chosen automatically; choosing it does nothing, so the detected look shows again.
            updating = true;
            try
            {
                UpdateLook(Services.Settings.Current);
            }
            finally
            {
                updating = false;
            }
        }
    }

    private void OnBulbSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: BulbSize size, Content: string name })
        {
            Update(s => s with { Lights = s.Lights with { Size = size } }, $"Change the bulb size to {name}");
        }
    }

    private void OnPixelsClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: SpriteStyle style })
        {
            Update(s => s with { Look = s.Look with { Pixels = style } }, style == SpriteStyle.Crisp ? "Use crisp pixels" : "Use smooth pixels");
        }
    }

    private void OnGlowChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GlowBox.SelectedItem is ComboBoxItem { Tag: GlowLevel glow, Content: string name })
        {
            Update(s => s with { Look = s.Look with { Glow = glow } }, $"Change the glow to {name}");
        }
    }

    private void OnSmoothFadingClick(object sender, RoutedEventArgs e)
    {
        bool on = SmoothFadingCheck.IsChecked == true;
        Update(s => s with { Look = s.Look with { SmoothFading = on } }, on ? "Turn Smooth Fading on" : "Turn Smooth Fading off");
    }

    private void OnSmoothMotionClick(object sender, RoutedEventArgs e)
    {
        bool on = SmoothMotionCheck.IsChecked == true;
        Update(s => s with { Look = s.Look with { SmoothSaverMotion = on } }, on ? "Turn Smooth Screen Saver Motion on" : "Turn Smooth Screen Saver Motion off");
    }

    private void OnAutoStartClick(object sender, RoutedEventArgs e)
    {
        bool on = AutoStartCheck.IsChecked == true;
        Update(s => s with { Startup = s.Startup with { Auto = on } }, on ? "Start Holiday Lights automatically" : "Don't start Holiday Lights automatically");
    }

    private void OnHotKeyCheckClick(object sender, RoutedEventArgs e)
    {
        bool location = ReferenceEquals(sender, LocationHotKeyCheck);
        bool on = ((CheckBox)sender).IsChecked == true;
        string name = location ? "Switch Between On Desktop and On Top" : "Turn the Lights On or Off";
        Update(
            s => s with
            {
                HotKeys = location
                    ? s.HotKeys with { Location = s.HotKeys.Location with { Enabled = on } }
                    : s.HotKeys with { Lights = s.HotKeys.Lights with { Enabled = on } },
            },
            $"Turn the {name} hot key {(on ? "on" : "off")}");
    }

    /// <summary>"Change...": the Change Hot Key dialog; the new combination is turned on.</summary>
    private void OnChangeHotKeyClick(object sender, RoutedEventArgs e)
    {
        if (host is null)
        {
            return;
        }

        bool location = ReferenceEquals(sender, LocationChangeButton);
        HotKeyAction action = location ? HotKeyAction.SwitchLocation : HotKeyAction.ToggleLights;
        HotKeyBinding current = location ? Services.Settings.Current.HotKeys.Location : Services.Settings.Current.HotKeys.Lights;
        if (ChangeHotKeyDialog.Show(host, Services, action, current) is not { } chosen)
        {
            return;
        }

        HotKeyBinding binding = chosen with { Enabled = true };
        Services.Settings.Update(
            s => s with { HotKeys = location ? s.HotKeys with { Location = binding } : s.HotKeys with { Lights = binding } },
            SettingsChange.Edit($"Change the hot key to {HotKeyText.Compact(binding)}"));
        host.Announce($"The hot key is now {HotKeyText.Spoken(binding)}.");
    }

    /// <summary>"Use Ctrl+Shift+&lt;key&gt; Anyway" (the one-time notice of 5.4 users; the key is the imported one).</summary>
    private void UseImportedHotKey()
    {
        string key = Services.Settings.Current.HotKeys.Location.Key;
        Services.Settings.Update(
            s => s with
            {
                HotKeys = s.HotKeys with { Location = s.HotKeys.Location with { Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Shift } },
                Onboarding = s.Onboarding with { HotKeyImportNoticeDismissed = true },
            },
            SettingsChange.Edit($"Use Ctrl+Shift+{key} as the hot key"));
    }

    private void DismissHotKeyNotice() =>
        Services.Settings.Update(s => s with { Onboarding = s.Onboarding with { HotKeyImportNoticeDismissed = true } }, SettingsChange.Internal);

    private void OnRestClick(object sender, RoutedEventArgs e)
    {
        bool on = ((CheckBox)sender).IsChecked == true;
        string label = (((CheckBox)sender).Content as string ?? "").Replace("_", "", StringComparison.Ordinal);
        Update(
            s => s with
            {
                Rest = ReferenceEquals(sender, RestFullScreenCheck) ? s.Rest with { FullScreen = on }
                    : ReferenceEquals(sender, RestPresentationCheck) ? s.Rest with { Presentation = on }
                    : ReferenceEquals(sender, MusicFullScreenCheck) ? s.Rest with { MusicFullScreen = on }
                    : ReferenceEquals(sender, MusicFocusCheck) ? s.Rest with { MusicFocus = on }
                    : s.Rest with { MusicLock = on },
            },
            $"{(on ? "Turn on" : "Turn off")} \"{label}\"");
    }

    private void OnEnergySaverChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnergySaverBox.SelectedItem is ComboBoxItem { Tag: EnergySaverChoice choice, Content: string name })
        {
            Update(s => s with { Rest = s.Rest with { EnergySaver = choice } }, $"When Energy Saver is on: {name}");
        }
    }

    private void OnLimitFlashingClick(object sender, RoutedEventArgs e)
    {
        bool on = LimitFlashingCheck.IsChecked == true;
        Update(s => s with { Accessibility = s.Accessibility with { LimitFlashing = on } }, on ? "Limit flashing" : "Don't limit flashing");
    }

    private void OnDecorateClick(object sender, RoutedEventArgs e)
    {
        bool on = DecorateCheck.IsChecked == true;
        Update(s => s with { Ui = s.Ui with { DecorateWindow = on } }, on ? "Decorate the Settings window with lights" : "Don't decorate the Settings window");
    }

    private void OnAssociateClick(object sender, RoutedEventArgs e)
    {
        bool on = AssociateCheck.IsChecked == true;
        Update(s => s with { Files = s.Files with { AssociateBul = on } }, on ? "Open bulb files with Holiday Lights" : "Don't open bulb files with Holiday Lights");
    }

    /// <summary>Opens My Bulbs, My Music or My Pictures (created if missing).</summary>
    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        string folder = (sender as FrameworkElement)?.Tag switch
        {
            "Music" => Services.Paths.MyMusicFolder,
            "Pictures" => Services.Paths.MyPicturesFolder,
            _ => Services.Paths.MyBulbsFolder,
        };
        try
        {
            Directory.CreateDirectory(folder);
            Services.Shell.OpenFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The path names the account (CONTRACTS 4; review r1 #75): only which folder it was.
            Services.Log.Warn("Settings.General", $"Could not open the My {(sender as FrameworkElement)?.Tag as string ?? "Bulbs"} folder.", ex);
            host?.ShowSnackbar($"Couldn't open the folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads whether 5.4 is installed and its leftovers (registry, Startup folder shortcuts, the running 5.4, the screen
    /// saver) and shows the leftover fixes: only when the page is shown, never on a settings change. A fixed leftover's
    /// bar closes itself.
    /// </summary>
    private void ReadLegacyState(AppSettings settings)
    {
        legacyPresent = Services.LegacyImporter.IsLegacyInstallPresent();
        Leftovers.Children.Clear();
        if (!legacyPresent && settings.Import54 is null)
        {
            return;
        }

        LegacyLeftoverState leftovers = Services.LegacyLeftovers.Detect();
        if (leftovers.IsRunning)
        {
            AddLeftover("Holiday Lights 5.4 is running too. It doesn't work properly on this version of Windows.", "Close Holiday Lights 5.4", () => Services.LegacyLeftovers.CloseRunningInstance());
        }

        if (leftovers.StartupShortcut is not null)
        {
            AddLeftover("Holiday Lights 5.4 also starts with Windows.", "Turn Off", () => Services.LegacyLeftovers.RemoveStartupShortcut());
        }

        if (Services.ScreenSaverRegistration.GetStatus().State == ScreenSaverState.Legacy54)
        {
            AddLeftover("Your screen saver is still set to Holiday Lights 5.4.", "Fix It", FixScreenSaver);
        }
    }

    /// <summary>The "Holiday Lights 5.4" group (only when the 5.4 key exists or 5.4 was imported): the import line.</summary>
    private void UpdateLegacyStatus(AppSettings settings)
    {
        LegacyCard.Visibility = legacyPresent || settings.Import54 is not null ? Visibility.Visible : Visibility.Collapsed;
        if (LegacyCard.Visibility != Visibility.Visible)
        {
            return;
        }

        if (settings.Import54 is { } record)
        {
            string date = record.Date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
            ImportStatus.Text = string.Create(CultureInfo.CurrentCulture,
                $"Imported on {date}: your settings, {Count(record.Themes, "theme")}, {Count(record.Bulbs, "bulb")}, {Count(record.Songs, "song")} and {Count(record.Pictures, "picture")}.");
        }
        else
        {
            ImportStatus.Text = "Your Holiday Lights 5.4 settings haven't been imported.";
        }

        ImportDetailsButton.IsEnabled = settings.Import54 is not null;
    }

    private static string Count(int count, string singular) =>
        count == 1 ? $"1 {singular}" : string.Create(CultureInfo.CurrentCulture, $"{count} {singular}s");

    private void AddLeftover(string text, string action, Func<bool> fix)
    {
        var bar = new InfoBar { Severity = InfoBarSeverity.Warning, Message = text, ActionText = action, IsClosable = false, Margin = new Thickness(0, 4, 0, 4) };
        bar.ActionCommand = new DelegateCommand(() =>
        {
            if (fix())
            {
                bar.IsOpen = false;
                host?.Announce("Fixed.");
            }
        });
        Leftovers.Children.Add(bar);
    }

    /// <summary>"Fix It": Holiday Lights becomes the screen saver, remembering the previous values (one undo step).</summary>
    private bool FixScreenSaver()
    {
        ScreenSaverPrevious previous = Services.ScreenSaverRegistration.Use();
        Services.Settings.Update(s => s with { Saver = s.Saver with { Previous = previous } }, SettingsChange.Internal);
        host?.History.Record(new UndoStep("Use the new screen saver", () => Services.ScreenSaverRegistration.StopUsing(previous), () => Services.ScreenSaverRegistration.Use()),
            Undo.UndoCancelBehavior.RevertOnCancel);
        return true;
    }

    private void OnImportDetailsClick(object sender, RoutedEventArgs e) => ShowImportDetails();

    private void ShowImportDetails()
    {
        if (host is not null && Services.Settings.Current.Import54 is { } record)
        {
            ImportDetailsDialog.Show(host, record);
        }
    }

    /// <summary>
    /// "Import Again...": confirmation, then the 5.4 settings replace the current ones (one undo step). Reading 5.4 and
    /// copying its files run on the thread pool like the first-run import (CONTRACTS 6.7: the add-on index may be waited
    /// for), with the button disabled; changes made meanwhile are kept unless the import replaces them.
    /// </summary>
    private async void OnImportAgainClick(object sender, RoutedEventArgs e)
    {
        if (host is null || importing)
        {
            return;
        }

        bool confirmed = await ContentDialogs.ShowAsync(host.Window, new ContentDialogOptions(
            "Import Holiday Lights 5.4 Settings Again?",
            "Your current bulb, music, screen saver and hot key settings will be replaced by the ones from Holiday Lights 5.4. Your themes are kept; 5.4 themes you don't have yet are added.",
            "Import",
            "Cancel",
            PrimaryIsDestructive: true));
        if (!confirmed)
        {
            return;
        }

        ISettingsHost window = host;
        ILegacyImporter importer = Services.LegacyImporter;
        AppSettings before = Services.Settings.Current;
        LegacyImportResult? result;
        importing = true;
        ImportAgainButton.IsEnabled = false;
        ImportBar.IsOpen = false;
        try
        {
            result = await Task.Run(() => importer.Analyze() is { } preview ? importer.Import(preview, before, LegacyImportMode.Again) : null).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            Services.Log.Warn("Settings.General", "Import Again failed.", ex);
            if (!disposed)
            {
                ImportBar.Message = $"Couldn't import your Holiday Lights 5.4 settings: {ex.Message}";
                ImportBar.IsOpen = true;
            }

            return;
        }
        finally
        {
            importing = false;
            ImportAgainButton.IsEnabled = true;
        }

        if (disposed)
        {
            return;
        }

        if (result is null)
        {
            window.ShowSnackbar("Holiday Lights 5.4 settings couldn't be found.");
            return;
        }

        AppSettings imported = result.Settings with { Import54 = result.Record };
        Services.Settings.Update(s => SettingsMerge.Apply(s, imported, before), new SettingsChange(SettingsChangeKind.Import, "Import Holiday Lights 5.4 settings again"));
        window.ShowSnackbar("Imported your Holiday Lights 5.4 settings.", "Undo", window.UndoLast);
        window.Announce("Imported your Holiday Lights 5.4 settings.");
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => _ = ResetAsync();

    /// <summary>"Reset All Settings...": confirmation, then the newcomer settings; Recent Settings keeps the current ones; snackbar "Settings reset." [Undo].</summary>
    private async Task ResetAsync()
    {
        if (host is null)
        {
            return;
        }

        bool confirmed = await ContentDialogs.ShowAsync(host.Window, new ContentDialogOptions(
            "Reset Holiday Lights?",
            "This puts every setting back the way it was when Holiday Lights was installed. Your themes, bulbs, songs and pictures are kept, and Recent Settings keeps your current settings.",
            "Reset",
            "Cancel",
            PrimaryIsDestructive: true));
        if (!confirmed)
        {
            return;
        }

        var context = new FirstRunContext(DateOnly.FromDateTime(DateTime.Now), Services.SystemInfo.RegionCode, Services.SystemInfo.AnimationsEnabled, DateTimeOffset.Now);
        Services.Settings.Update(
            s => NewcomerSettings.Create(s, context, Services.Calendar, Services.Themes, Services.ThemeService, "Before Reset"),
            new SettingsChange(SettingsChangeKind.Reset, "Reset all settings"));
        host.ShowSnackbar("Settings reset.", "Undo", host.UndoLast);
        host.Announce("Settings reset.");
    }

    private void OnRestoreThemesClick(object sender, RoutedEventArgs e)
    {
        if (host is not null)
        {
            RestoreThemesDialog.Show(host, Services);
        }
    }

    /// <summary>"Uninstall Holiday Lights...": confirmation, then the per-user uninstaller (the program exits).</summary>
    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (host is null)
        {
            return;
        }

        bool confirmed = await ContentDialogs.ShowAsync(host.Window, new ContentDialogOptions(
            "Uninstall Holiday Lights?",
            "Uninstalling removes Holiday Lights from your PC. Your bulbs, songs and pictures in Documents\\Holiday Lights are kept unless you choose to remove them.",
            "Uninstall",
            "Cancel",
            PrimaryIsDestructive: true));
        if (confirmed)
        {
            Services.AppShell.StartUninstall();
        }
    }
}
