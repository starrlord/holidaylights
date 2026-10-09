using System.Windows;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>The "New Category" dialog (PRODUCT-SPEC 3.3.2).</summary>
internal sealed partial class NewCategoryWindow : Window
{
    /// <summary>Creates the dialog.</summary>
    public NewCategoryWindow()
    {
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>The typed name.</summary>
    public NewCategoryViewModel ViewModel { get; } = new();

    /// <summary>Shows the dialog.</summary>
    /// <param name="owner">The window it belongs to.</param>
    /// <returns>The typed name, or null when cancelled.</returns>
    public static string? Ask(Window owner)
    {
        var window = new NewCategoryWindow { Owner = owner };
        return window.ShowDialog() == true ? window.ViewModel.Name : null;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanCreate)
        {
            DialogResult = true;
        }
    }
}

/// <summary>The state of "New Category": OK is enabled when the name has a letter or digit (5.4 <c>_isctype(c, 0x107)</c>).</summary>
internal sealed class NewCategoryViewModel : ObservableObject
{
    private string name = "";

    /// <summary>The typed name.</summary>
    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value ?? ""))
            {
                OnPropertyChanged(nameof(CanCreate));
            }
        }
    }

    /// <summary>True when "OK" is enabled.</summary>
    public bool CanCreate => BulCategories.IsUsableName(name);
}
