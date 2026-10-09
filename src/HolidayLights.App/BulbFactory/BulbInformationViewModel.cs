using System.Globalization;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// The "Bulb Information" group of Bulb Editing (PRODUCT-SPEC 3.3.1): name and description (one line, 79 characters each,
/// live counters; the name is required), author and copyright (two lines, 79 characters including the line break), and the
/// categories with "New Category...".
/// </summary>
internal sealed class BulbInformationViewModel : ObservableObject
{
    private readonly INewCategoryPrompt prompt;
    private string name;
    private string description;
    private string author;
    private string copyright;

    /// <summary>Creates the group for a document.</summary>
    /// <param name="document">The bulb as opened.</param>
    /// <param name="knownCategories">Every known category.</param>
    /// <param name="prompt">Shows "New Category".</param>
    public BulbInformationViewModel(BulbDocument document, IEnumerable<string> knownCategories, INewCategoryPrompt prompt)
    {
        this.prompt = prompt;
        name = document.Name;
        description = document.Description;
        author = document.Author;
        copyright = document.Copyright;
        Categories = new CategoryChecklist(knownCategories, [.. document.Categories]);
        Categories.Changed += (_, _) => OnChanged(nameof(CategoryError), nameof(CharacterWarning));
        NewCategoryCommand = new RelayCommand(AddCategory);
    }

    /// <summary>Raised after any field or category changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after "New Category..." added or checked a row (the view brings it into view).</summary>
    public event EventHandler<CategoryItem>? CategoryAdded;

    /// <summary>"Bulb Name".</summary>
    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value ?? ""))
            {
                OnChanged(nameof(NameCounter), nameof(NameError), nameof(CharacterWarning));
            }
        }
    }

    /// <summary>"Description".</summary>
    public string Description
    {
        get => description;
        set
        {
            if (SetProperty(ref description, value ?? ""))
            {
                OnChanged(nameof(DescriptionCounter), nameof(CharacterWarning));
            }
        }
    }

    /// <summary>"Author's Name and E-Mail Address".</summary>
    public string Author
    {
        get => author;
        set
        {
            if (SetProperty(ref author, value ?? ""))
            {
                OnChanged(nameof(CharacterWarning));
            }
        }
    }

    /// <summary>"Copyright Message".</summary>
    public string Copyright
    {
        get => copyright;
        set
        {
            if (SetProperty(ref copyright, value ?? ""))
            {
                OnChanged(nameof(CharacterWarning));
            }
        }
    }

    /// <summary>The live counter of the name ("4/79").</summary>
    public string NameCounter => Counter(name);

    /// <summary>The live counter of the description.</summary>
    public string DescriptionCounter => Counter(description);

    /// <summary>"Give your bulb a name." while the name is empty, else null.</summary>
    public string? NameError => string.IsNullOrWhiteSpace(name) ? BulbFactoryStrings.NameRequired : null;

    /// <summary>A warning while a text holds characters that a bulb file stores as "?", else null.</summary>
    public string? CharacterWarning =>
        new[] { name, description, author, copyright }.All(BulText.CanStore) && Categories.CheckedNames.All(BulText.CanStore)
            ? null
            : BulbFactoryStrings.CharactersWillBeReplaced;

    /// <summary>A problem while the checked categories do not fit in a bulb file, else null.</summary>
    public string? CategoryError => BulCategories.FitInFile(Categories.CheckedNames) ? null : BulbFactoryStrings.TooManyCategories;

    /// <summary>The category check list.</summary>
    public CategoryChecklist Categories { get; }

    /// <summary>"New Category...".</summary>
    public RelayCommand NewCategoryCommand { get; }

    /// <summary>Copies the fields to a document (the name trimmed, line breaks as "\r\n" like 5.4 stored them).</summary>
    /// <param name="document">The document to fill.</param>
    public void ApplyTo(BulbDocument document)
    {
        document.Name = name.Trim();
        document.Description = description;
        document.Author = WindowsLineBreaks(author);
        document.Copyright = WindowsLineBreaks(copyright);
        document.Categories.Clear();
        foreach (string category in Categories.CheckedNames)
        {
            document.Categories.Add(category);
        }
    }

    private static string Counter(string text) =>
        string.Create(CultureInfo.CurrentCulture, $"{text.EnumerateRunes().Count()}/{BulText.MaxFieldLength}");

    private static string WindowsLineBreaks(string text) => text.ReplaceLineEndings("\r\n");

    private void AddCategory()
    {
        if (prompt.AskNewCategory() is { } typed && Categories.Add(typed) is { } item)
        {
            CategoryAdded?.Invoke(this, item);
        }
    }

    private void OnChanged(params string[] properties)
    {
        foreach (string property in properties)
        {
            OnPropertyChanged(property);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
