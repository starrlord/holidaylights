using System.Collections.ObjectModel;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>One row of the category check list.</summary>
internal sealed class CategoryItem : ObservableObject
{
    private bool isChecked;

    /// <summary>Creates the row.</summary>
    /// <param name="name">The category.</param>
    /// <param name="isChecked">Whether the bulb is in it.</param>
    public CategoryItem(string name, bool isChecked)
    {
        Name = name;
        this.isChecked = isChecked;
    }

    /// <summary>Raised when <see cref="IsChecked"/> changed.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>The category name.</summary>
    public string Name { get; }

    /// <summary>Whether the bulb is in the category.</summary>
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (SetProperty(ref isChecked, value))
            {
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

/// <summary>
/// The "Categories" check list of Bulb Editing and Edit Categories: every known category
/// A-Z (the internal <c>_0</c>/<c>_1</c> hidden), the bulb's own checked; "New Category..." adds one checked. Names are
/// compared ignoring case. Categories created here exist only in this list until it is saved (5.4).
/// </summary>
internal sealed class CategoryChecklist
{
    private readonly IReadOnlyList<string> original;

    /// <summary>Creates the list.</summary>
    /// <param name="known">Every known category (<see cref="IBulbCatalog.GetAllCategoryNames"/>).</param>
    /// <param name="bulbCategories">The bulb's categories, in the order they are stored.</param>
    public CategoryChecklist(IEnumerable<string> known, IReadOnlyList<string> bulbCategories)
    {
        original = bulbCategories;
        var checkedNames = new HashSet<string>(bulbCategories, StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in known.Concat(bulbCategories))
        {
            if (!string.IsNullOrWhiteSpace(name) && name is not ("_0" or "_1"))
            {
                names.TryAdd(name, name);
            }
        }

        Items = [.. names.Values.Order(StringComparer.CurrentCultureIgnoreCase).Select(n => NewItem(n, checkedNames.Contains(n)))];
    }

    /// <summary>Raised when a row was checked or unchecked or a category was added.</summary>
    public event EventHandler? Changed;

    /// <summary>The rows, A-Z.</summary>
    public ObservableCollection<CategoryItem> Items { get; }

    /// <summary>
    /// The checked categories: the bulb's own still checked ones first, in their stored order and spelling, then the newly
    /// checked ones in list order (so saving an unchanged list changes nothing).
    /// </summary>
    public IReadOnlyList<string> CheckedNames
    {
        get
        {
            var chosen = new HashSet<string>(Items.Where(i => i.IsChecked).Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            List<string> names = [.. original.Where(chosen.Contains).Distinct(StringComparer.OrdinalIgnoreCase)];
            var kept = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            names.AddRange(Items.Where(i => i.IsChecked && !kept.Contains(i.Name)).Select(i => i.Name));
            return names;
        }
    }

    /// <summary>"New Category": checks an existing category (ignoring case) or adds a new one, checked, in A-Z order.</summary>
    /// <param name="typed">The typed name (normalized here with <see cref="BulCategories.Normalize"/>).</param>
    /// <returns>The checked row, or null when the name has no letter or digit.</returns>
    public CategoryItem? Add(string typed)
    {
        string name = BulCategories.Normalize(typed);
        if (!BulCategories.IsUsableName(name))
        {
            return null;
        }

        CategoryItem? item = Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = NewItem(name, true);
            int index = 0;
            while (index < Items.Count && StringComparer.CurrentCultureIgnoreCase.Compare(Items[index].Name, name) < 0)
            {
                index++;
            }

            Items.Insert(index, item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            item.IsChecked = true;
        }

        return item;
    }

    private CategoryItem NewItem(string name, bool isChecked)
    {
        var item = new CategoryItem(name, isChecked);
        item.CheckedChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }
}
