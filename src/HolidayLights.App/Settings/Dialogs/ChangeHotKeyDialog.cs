using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Change Hot Key dialog (PRODUCT-SPEC 3.8.2): "Press the keys you want to use for:" the job, a recording field, the
/// message line (refusals disable Save; warnings do not), "Use Default", [Save] [Cancel]. Esc cancels; Tab leaves the field.
/// </summary>
public sealed class ChangeHotKeyDialog : DialogWindow
{
    private readonly IAppServices services;
    private readonly HotKeyAction action;
    private readonly HotKeyRecorder recorder;
    private readonly TextBlock message;
    private readonly Button saveButton;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="services">The services (validation).</param>
    /// <param name="action">The hot key's job.</param>
    /// <param name="current">The current combination.</param>
    public ChangeHotKeyDialog(Window? owner, IAppServices services, HotKeyAction action, HotKeyBinding current)
        : base(owner, "Change Hot Key")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(current);
        this.services = services;
        this.action = action;
        var prompt = new TextBlock { Text = "Press the keys you want to use for:", TextWrapping = TextWrapping.Wrap };
        var job = new TextBlock { Text = JobName(action), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 12) };
        recorder = new HotKeyRecorder { Binding = current, MinHeight = 56, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(recorder, "Hot key for " + JobName(action));
        message = Secondary("");
        message.Margin = new Thickness(0, 10, 0, 0);
        message.MinHeight = 34;
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        Body = new StackPanel { Width = 440, Children = { prompt, job, recorder, message } };
        AddLeftButton("Use _Default", UseDefault);
        saveButton = AddButton("Save", isPrimary: true, isCancel: false, Save);
        AddButton("Cancel", isPrimary: false, isCancel: true, Close);
        recorder.Recorded += (_, binding) => Check(binding);
        Loaded += (_, _) => recorder.Focus();
        Check(current);
    }

    /// <summary>The combination chosen, or null when cancelled.</summary>
    public HotKeyBinding? Chosen { get; private set; }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="services">The services.</param>
    /// <param name="action">The hot key's job.</param>
    /// <param name="current">The current combination.</param>
    /// <returns>The new combination, or null.</returns>
    public static HotKeyBinding? Show(ISettingsHost host, IAppServices services, HotKeyAction action, HotKeyBinding current)
    {
        ArgumentNullException.ThrowIfNull(host);
        var dialog = new ChangeHotKeyDialog(host.Window, services, action, current);
        dialog.ShowDialog();
        return dialog.Chosen;
    }

    /// <summary>The job of a hot key as the dialog names it.</summary>
    /// <param name="action">The job.</param>
    /// <returns>"Switch Between On Desktop and On Top" or "Turn the Lights On or Off".</returns>
    public static string JobName(HotKeyAction action) =>
        action == HotKeyAction.ToggleLights ? "Turn the Lights On or Off" : "Switch Between On Desktop and On Top";

    /// <summary>The message line of a verdict (PRODUCT-SPEC 3.8.2), or "" when there is nothing to say.</summary>
    /// <param name="check">The verdict.</param>
    /// <param name="binding">The combination.</param>
    /// <returns>The sentence.</returns>
    public static string MessageFor(HotKeyCheck check, HotKeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(binding);
        string combination = HotKeyText.Compact(binding);
        return check.Validity switch
        {
            HotKeyValidity.KeyNotAllowed => "Holiday Lights can't use that key. Use a letter, a number, F1-F24, Insert, Home, End, Page Up, Page Down or Pause.",
            HotKeyValidity.NeedsModifier => "Add Ctrl, Alt or the Windows key to that key.",
            HotKeyValidity.ShiftLetterOnly => "That would stop you from typing capital letters.",
            HotKeyValidity.InUse => "Another program is using this combination.",
            HotKeyValidity.TypesCharacter => $"This combination types \"{check.TypedCharacter}\" on your {check.LayoutName} keyboard. Choose another.",
            HotKeyValidity.BrowserShortcut => binding.Key == "B"
                ? $"{combination} is the bookmarks-bar shortcut in web browsers. Holiday Lights would take it from them."
                : $"{combination} is a shortcut in web browsers. Holiday Lights would take it from them.",
            HotKeyValidity.WindowsKeyCombination => "Windows uses many Windows-key shortcuts; this one might stop working.",
            _ => "",
        };
    }

    private void Check(HotKeyBinding binding)
    {
        HotKeyCheck check = services.HotKeys.Validate(binding, action);
        saveButton.IsEnabled = check.CanSave;
        message.Text = MessageFor(check, binding);
        message.SetResourceReference(TextBlock.ForegroundProperty, check.CanSave ? "TextFillColorSecondaryBrush" : "SystemFillColorCriticalBrush");
    }

    private void UseDefault()
    {
        var binding = new HotKeyBinding
        {
            Enabled = true,
            Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift,
            Key = action == HotKeyAction.ToggleLights ? "L" : "B",
        };
        recorder.Binding = binding;
        Check(binding);
        Announcer.Announce(recorder, HotKeyText.Spoken(binding));
    }

    private void Save()
    {
        if (recorder.Binding is { } binding && services.HotKeys.Validate(binding, action).CanSave)
        {
            Chosen = binding;
            Close();
        }
    }
}
