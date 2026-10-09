using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace HolidayLights.App.BulbFactory;

/// <summary>The "Edit Categories" dialog (PRODUCT-SPEC 3.3.2).</summary>
internal sealed partial class EditCategoriesWindow : Window, INewCategoryPrompt
{
    /// <summary>Creates the dialog for a bulb.</summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="bulb">The bulb's metadata.</param>
    public EditCategoriesWindow(IBulbCatalog catalog, BulbInfo bulb)
    {
        ViewModel = new EditCategoriesViewModel(catalog, bulb, this);
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.Saved += (_, _) => DialogResult = true;
        ViewModel.CategoryAdded += (_, item) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (CategoryList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });
        Loaded += (_, _) => CategoryList.Focus();
    }

    /// <summary>The dialog state.</summary>
    public EditCategoriesViewModel ViewModel { get; }

    /// <inheritdoc />
    public string? AskNewCategory() => NewCategoryWindow.Ask(this);

    /// <summary>The list is one tab stop; focus lands on the first check box.</summary>
    private void OnCategoryListFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, CategoryList))
        {
            CategoryList.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }
}
