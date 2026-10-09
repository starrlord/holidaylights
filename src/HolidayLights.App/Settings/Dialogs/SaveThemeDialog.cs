using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Save Theme dialog (PRODUCT-SPEC 3.6.3, 5.4): "Save the current bulb, music and screen saver settings as:" with
/// the last loaded or saved theme name, all selected. Save stays disabled until the name is valid; an existing name asks
/// "Replace Theme?". Saving writes the 13 current values as one undo step.
/// </summary>
public sealed class SaveThemeDialog : DialogWindow
{
    /// <summary>The note for invalid characters.</summary>
    public const string InvalidCharactersText = "A theme name can't contain any of these characters: \\ / : * ? \" < > |";

    /// <summary>The note for a name that is too long.</summary>
    public const string TooLongText = "A theme name can have at most 63 characters.";

    private readonly IAppServices services;
    private readonly UndoHistory? history;
    private readonly TextBox nameBox;
    private readonly TextBlock note;
    private readonly Button saveButton;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The Settings window.</param>
    /// <param name="services">The services.</param>
    /// <param name="history">The Settings window's history (the save is one step), or null.</param>
    public SaveThemeDialog(Window? owner, IAppServices services, UndoHistory? history)
        : base(owner, "Save Theme")
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        this.history = history;
        var prompt = new TextBlock { Text = "Save the current bulb, music and screen saver settings as:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        nameBox = new TextBox { Text = services.Settings.Current.Themes.LastName, MaxLength = 100 };
        AutomationProperties.SetLabeledBy(nameBox, prompt);
        note = Secondary("");
        note.Margin = new Thickness(0, 6, 0, 0);
        note.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        AutomationProperties.SetLiveSetting(note, AutomationLiveSetting.Polite);
        Body = new StackPanel { Width = 420, Children = { prompt, nameBox, note } };
        saveButton = AddButton("Save", isPrimary: true, isCancel: false, OnSave);
        AddButton("Cancel", isPrimary: false, isCancel: true, Close);
        nameBox.TextChanged += (_, _) => Validate();
        Loaded += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
        Validate();
    }

    /// <summary>The name the settings were saved as, or null when cancelled.</summary>
    public string? SavedName { get; private set; }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="services">The services.</param>
    /// <returns>The saved theme's name, or null.</returns>
    public static string? Show(ISettingsHost host, IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(host);
        var dialog = new SaveThemeDialog(host.Window, services, host.History);
        dialog.ShowDialog();
        if (dialog.SavedName is { } name)
        {
            host.Announce($"Saved the {name} theme.");
        }

        return dialog.SavedName;
    }

    /// <summary>The note under the name box while it shows something (an invalid name, a failed save), else null.</summary>
    internal string? Note => note.Visibility == Visibility.Visible ? note.Text : null;

    /// <summary>Saves the theme under the name in the box, as the Save button does (tests).</summary>
    /// <returns>When the save finished, failed or was declined.</returns>
    internal Task SaveForTestAsync() => SaveAsync();

    private void Validate()
    {
        ThemeNameCheck check = services.ThemeService.ValidateName(nameBox.Text);
        saveButton.IsEnabled = check == ThemeNameCheck.Valid;
        ShowNote(check switch
        {
            ThemeNameCheck.InvalidCharacters => InvalidCharactersText,
            ThemeNameCheck.TooLong => TooLongText,
            _ => "",
        });
    }

    /// <summary>The Save button. Awaited here so that an unexpected failure reaches the dispatcher's handler instead of vanishing with the task.</summary>
    private async void OnSave() => await SaveAsync();

    private void ShowNote(string text)
    {
        note.Text = text;
        note.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task SaveAsync()
    {
        string name = nameBox.Text.Trim();
        if (services.ThemeService.ValidateName(name) != ThemeNameCheck.Valid)
        {
            return;
        }

        ThemeDefinition? previous = services.Themes.Find(name);
        if (previous is not null)
        {
            bool replace = await ContentDialogs.ShowAsync(this, new ContentDialogOptions(
                "Replace Theme?",
                $"A theme named \"{previous.Name}\" already exists.\n\nDo you want to replace it?",
                "Yes",
                "No",
                PrimaryIsDestructive: false));
            if (!replace)
            {
                nameBox.Focus();
                nameBox.SelectAll();
                return;
            }

            name = previous.Name;
        }

        ThemeDefinition theme = services.ThemeService.Capture(name, services.Settings.Current);
        try
        {
            services.Themes.Save(theme);
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            // The dialog stays open with the reason, so another name or a second try is one step away (PRODUCT-SPEC 5.13).
            services.Log.Warn("Settings.Themes", $"The {name} theme could not be saved.", exception);
            ShowNote(FileProblems.Sentence($"Couldn't save the {name} theme", exception));
            nameBox.Focus();
            return;
        }

        HeldItem? held = null;
        history?.Record(new UndoStep(
            $"Save the {name} theme",
            () =>
            {
                if (previous is not null)
                {
                    services.Themes.Save(previous);
                }
                else
                {
                    held = services.Themes.Delete(name);
                }
            },
            () =>
            {
                if (held is not null)
                {
                    services.Themes.Restore(held);
                }
                else
                {
                    services.Themes.Save(theme);
                }
            }));
        services.Settings.Update(s => s with { Themes = s.Themes with { LastName = name } }, SettingsChange.Internal);
        SavedName = name;
        Close();
    }
}
