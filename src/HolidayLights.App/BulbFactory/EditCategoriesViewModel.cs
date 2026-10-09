namespace HolidayLights.App.BulbFactory;

/// <summary>
/// "Edit Categories" (PRODUCT-SPEC 3.3.2): the categories of a built-in bulb or of someone else's bulb, stored in settings
/// keyed by bulb id (<c>bulbs.categoryOverrides</c>, one Undo step in Settings); the bulb file is never touched.
/// </summary>
/// <remarks>UI thread.</remarks>
internal sealed class EditCategoriesViewModel : ObservableObject
{
    private readonly IBulbCatalog catalog;
    private readonly string bulbId;
    private readonly INewCategoryPrompt prompt;

    /// <summary>Creates the dialog state for a bulb.</summary>
    /// <param name="catalog">The catalog (known categories, the override).</param>
    /// <param name="bulb">The bulb's metadata (its effective categories are checked).</param>
    /// <param name="prompt">Shows "New Category".</param>
    public EditCategoriesViewModel(IBulbCatalog catalog, BulbInfo bulb, INewCategoryPrompt prompt)
    {
        this.catalog = catalog;
        this.prompt = prompt;
        bulbId = bulb.Id;
        Title = $"{BulbFactoryStrings.EditCategoriesCaption} - {bulb.Name}";
        Categories = new CategoryChecklist(catalog.GetAllCategoryNames(), bulb.Categories);
        NewCategoryCommand = new RelayCommand(AddCategory);
        SaveCommand = new RelayCommand(Save);
    }

    /// <summary>Raised after "Save" stored the categories.</summary>
    public event EventHandler? Saved;

    /// <summary>Raised after "New..." added or checked a row.</summary>
    public event EventHandler<CategoryItem>? CategoryAdded;

    /// <summary>"Edit Categories - Star".</summary>
    public string Title { get; }

    /// <summary>The check list.</summary>
    public CategoryChecklist Categories { get; }

    /// <summary>"New...".</summary>
    public RelayCommand NewCategoryCommand { get; }

    /// <summary>"Save".</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>True after a save.</summary>
    public bool IsSaved { get; private set; }

    private void AddCategory()
    {
        if (prompt.AskNewCategory() is { } typed && Categories.Add(typed) is { } item)
        {
            CategoryAdded?.Invoke(this, item);
        }
    }

    private void Save()
    {
        catalog.SetCategoryOverrides(bulbId, Categories.CheckedNames);
        IsSaved = true;
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
