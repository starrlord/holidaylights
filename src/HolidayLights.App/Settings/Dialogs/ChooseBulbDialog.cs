using System.Windows;
using System.Windows.Controls;
using HolidayLights.App.Gallery;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Choose a Bulb dialog (PRODUCT-SPEC 3.8.4): the Bulb List (search, Show, Sort, Tiles/Details, the selected bulb's
/// details) limited to add-on bulbs (5.4 allowed add-on bulbs as animations); [Choose] [Cancel]; double-click or Enter
/// chooses.
/// </summary>
public sealed class ChooseBulbDialog : DialogWindow
{
    private readonly BulbList list;
    private readonly SelectedBulbBar bar;
    private readonly Button chooseButton;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="services">The services.</param>
    /// <param name="selectedId">The bulb to select at first, or null.</param>
    public ChooseBulbDialog(Window? owner, IAppServices services, string? selectedId)
        : base(owner, "Choose a Bulb")
    {
        ArgumentNullException.ThrowIfNull(services);
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        Width = 900;
        Height = 640;
        MinWidth = 600;
        MinHeight = 480;
        list = new BulbList { Services = services, IsChooser = true };
        bar = new SelectedBulbBar { Services = services, List = list, ShowCommands = false, Margin = new Thickness(0, 8, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bar, 1);
        root.Children.Add(list);
        root.Children.Add(bar);
        Body = root;
        chooseButton = AddButton("Choose", isPrimary: true, isCancel: false, Choose);
        AddButton("Cancel", isPrimary: false, isCancel: true, Close);
        list.SelectionChanged += (_, _) =>
        {
            bar.Show(list.SelectedItems, services.Settings.Current.Current.Arrangement);
            chooseButton.IsEnabled = list.SelectedItem is not null;
        };
        list.ActionRequested += (_, request) =>
        {
            if (request.Action == BulbAction.Use)
            {
                Choose();
            }
        };
        chooseButton.IsEnabled = false;
        Loaded += (_, _) =>
        {
            if (selectedId is not null && list.Select(selectedId))
            {
                list.FocusList();
            }
            else
            {
                list.FocusSearch();
            }
        };
    }

    /// <summary>The chosen bulb, or null when cancelled.</summary>
    public string? ChosenId { get; private set; }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="services">The services.</param>
    /// <param name="selectedId">The bulb to select at first, or null.</param>
    /// <returns>The chosen bulb, or null.</returns>
    public static string? Show(ISettingsHost host, IAppServices services, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(host);
        var dialog = new ChooseBulbDialog(host.Window, services, selectedId);
        dialog.ShowDialog();
        return dialog.ChosenId;
    }

    private void Choose()
    {
        if (list.SelectedItem is { } item)
        {
            ChosenId = item.Id;
            Close();
        }
    }
}
