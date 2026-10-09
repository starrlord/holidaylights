using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Restore Built-In Themes dialog (PRODUCT-SPEC 3.6.5): the shipped themes that are missing or differ from the
/// original, all checked: "Bring back these themes as they were originally:" [Restore] [Cancel]. Restoring overwrites
/// same-named themes as one undo step.
/// </summary>
public sealed class RestoreThemesDialog : DialogWindow
{
    private readonly IAppServices services;
    private readonly ISettingsHost? host;
    private readonly List<(CheckBox Box, string Name)> rows = [];
    private readonly Button? restoreButton;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="services">The services.</param>
    /// <param name="host">The Settings window (undo, snackbar), or null.</param>
    public RestoreThemesDialog(Window? owner, IAppServices services, ISettingsHost? host)
        : base(owner, "Restore Built-In Themes")
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        this.host = host;
        IReadOnlyList<ThemeDefinition> themes = services.Themes.GetMissingOrChangedShipped();
        var panel = new StackPanel { Width = 420 };
        if (themes.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "All built-in themes are installed as they were originally.", TextWrapping = TextWrapping.Wrap });
            Body = panel;
            AddButton("Close", isPrimary: true, isCancel: true, Close);
            return;
        }

        panel.Children.Add(new TextBlock { Text = "Bring back these themes as they were originally:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var list = new StackPanel();
        foreach (ThemeDefinition theme in themes.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool exists = services.Themes.Find(theme.Name) is not null;
            var box = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2),
                Content = exists ? $"{theme.Name} (changed)" : $"{theme.Name} (missing)",
            };
            System.Windows.Automation.AutomationProperties.SetName(box, theme.Name);
            // Checked/Unchecked too: UI Automation's Toggle raises no Click (review r1 #34).
            box.Checked += (_, _) => UpdateButton();
            box.Unchecked += (_, _) => UpdateButton();
            rows.Add((box, theme.Name));
            list.Children.Add(box);
        }

        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Body = panel;
        restoreButton = AddButton("Restore", isPrimary: true, isCancel: false, Restore);
        AddButton("Cancel", isPrimary: false, isCancel: true, Close);
        Loaded += (_, _) => restoreButton.Focus();
    }

    /// <summary>The number of themes restored (0 when cancelled).</summary>
    public int Restored { get; private set; }

    /// <summary>The sentence of a failed restore ("Couldn't restore Halloween: &lt;Windows reason&gt;"), or null.</summary>
    internal string? Problem { get; private set; }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="services">The services.</param>
    /// <returns>The number of themes restored.</returns>
    public static int Show(ISettingsHost host, IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(host);
        var dialog = new RestoreThemesDialog(host.Window, services, host);
        dialog.ShowDialog();
        return dialog.Restored;
    }

    private void UpdateButton()
    {
        if (restoreButton is not null)
        {
            restoreButton.IsEnabled = rows.Any(r => r.Box.IsChecked == true);
        }
    }

    /// <summary>Restores the checked themes, as the Restore button does (tests).</summary>
    internal void RestoreForTest() => Restore();

    private void Restore()
    {
        string[] names = [.. rows.Where(r => r.Box.IsChecked == true).Select(r => r.Name)];
        if (names.Length == 0)
        {
            return;
        }

        Dictionary<string, ThemeDefinition?> before = names.ToDictionary(n => n, n => services.Themes.Find(n), StringComparer.CurrentCultureIgnoreCase);
        Exception? failure = null;
        try
        {
            services.Themes.RestoreShipped(names);
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            failure = exception;
        }

        // A failed write can leave some themes restored: those, and only those, become the undo step.
        string[] restored = failure is null ? names : [.. names.Where(IsOriginal)];
        if (restored.Length > 0 && host is not null)
        {
            string text = restored.Length == 1 ? "Restored 1 theme." : string.Create(CultureInfo.CurrentCulture, $"Restored {restored.Length} themes.");
            host.History.Record(new UndoStep(
                restored.Length == 1 ? $"Restore {restored[0]}" : string.Create(CultureInfo.CurrentCulture, $"Restore {restored.Length} themes"),
                () =>
                {
                    foreach (string name in restored)
                    {
                        if (before[name] is { } previous)
                        {
                            services.Themes.Save(previous);
                        }
                        else
                        {
                            _ = services.Themes.Delete(name);
                        }
                    }
                },
                () => services.Themes.RestoreShipped(restored)));
            host.ShowSnackbar(text, "Undo", host.UndoLast);
            host.Announce(text);
        }

        Restored = restored.Length;
        if (failure is not null)
        {
            int failed = names.Length - restored.Length;
            string what = names.Length == 1
                ? $"Couldn't restore {names[0]}"
                : restored.Length == 0
                    ? string.Create(CultureInfo.CurrentCulture, $"Couldn't restore {names.Length} themes")
                    : string.Create(CultureInfo.CurrentCulture, $"Couldn't restore {failed} of {names.Length} themes");
            services.Log.Warn("Settings.Themes", what + ".", failure);
            Problem = FileProblems.Sentence(what, failure);
            host?.ShowMessage(Problem, Controls.InfoBarSeverity.Error);
        }

        Close();
    }

    /// <summary>True when the theme of that name is installed with its original values.</summary>
    private bool IsOriginal(string name) => services.Themes.Find(name) is { } theme && !services.Themes.IsChangedFromOriginal(theme);
}
