using System.Globalization;

namespace HolidayLights.App.Gallery;

/// <summary>One item of the "Show:" filter (PRODUCT-SPEC 3.2.5): "All Bulbs (1,550)", "Christmas (212)".</summary>
/// <param name="Key">A stable key ("all", "favorites", "category:christmas"), kept across rebuilds of the list.</param>
/// <param name="Filter">The catalog filter.</param>
/// <param name="Label">The label without the count ("For Halloween", "Christmas").</param>
/// <param name="Count">The number of bulbs.</param>
/// <param name="Categories">The categories of a category or holiday filter.</param>
public sealed record ShowOption(string Key, BulbFilter Filter, string Label, int Count, IReadOnlyList<string> Categories)
{
    /// <summary>The key of "All Bulbs".</summary>
    public const string AllKey = "all";

    /// <summary>The key of "Favorites".</summary>
    public const string FavoritesKey = "favorites";

    /// <summary>The key of "In Use".</summary>
    public const string InUseKey = "inuse";

    /// <summary>The key of "My Bulbs".</summary>
    public const string MyBulbsKey = "my";

    /// <summary>The key of "Removed Bulbs".</summary>
    public const string RemovedKey = "removed";

    /// <summary>"All Bulbs (1,550)".</summary>
    public string Text => string.Create(CultureInfo.CurrentCulture, $"{Label} ({Count:N0})");

    /// <summary>The query of this option.</summary>
    /// <param name="search">The search text.</param>
    /// <param name="sort">The sort order.</param>
    /// <param name="inUse">The bulbs on the screen.</param>
    /// <param name="addOnsOnly">True in the Choose a Bulb dialog.</param>
    /// <returns>The query.</returns>
    public BulbQuery ToQuery(string search, BulbSortOrder sort, IReadOnlySet<string> inUse, bool addOnsOnly) => new()
    {
        Filter = Filter,
        Categories = Categories,
        InUseIds = inUse,
        SearchText = search,
        Sort = sort,
        AddOnsOnly = addOnsOnly,
    };

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>Builds the "Show:" items with live counts.</summary>
public static class BulbShowOptions
{
    /// <summary>
    /// The items in order: All Bulbs, Favorites, In Use, For &lt;Holiday&gt; (while today is in a checked holiday with
    /// bulb categories), Built-In Bulbs, Add-On Bulbs, My Bulbs, then every non-empty category A-Z, then Removed Bulbs
    /// when there are any. A null entry stands for the separator before the categories.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="inUse">The bulbs on the screen.</param>
    /// <param name="holiday">The current holiday's name and bulb categories, or null.</param>
    /// <param name="addOnsOnly">True in the Choose a Bulb dialog (no Built-In Bulbs).</param>
    /// <returns>The items; null is the separator.</returns>
    public static IReadOnlyList<ShowOption?> Build(IBulbCatalog catalog, IReadOnlySet<string> inUse, (string Name, IReadOnlyList<string> Categories)? holiday, bool addOnsOnly)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(inUse);
        int Count(BulbFilter filter, IReadOnlyList<string>? categories = null) =>
            catalog.Query(new BulbQuery { Filter = filter, Categories = categories ?? [], InUseIds = inUse, AddOnsOnly = addOnsOnly }).Count;

        var items = new List<ShowOption?>
        {
            new(ShowOption.AllKey, BulbFilter.All, "All Bulbs", Count(BulbFilter.All), []),
            new(ShowOption.FavoritesKey, BulbFilter.Favorites, "Favorites", Count(BulbFilter.Favorites), []),
            new(ShowOption.InUseKey, BulbFilter.InUse, "In Use", Count(BulbFilter.InUse), []),
        };
        if (holiday is { } h && h.Categories.Count > 0)
        {
            items.Add(new("holiday", BulbFilter.Holiday, "For " + h.Name, Count(BulbFilter.Holiday, h.Categories), h.Categories));
        }

        if (!addOnsOnly)
        {
            items.Add(new("builtin", BulbFilter.BuiltIn, "Built-In Bulbs", Count(BulbFilter.BuiltIn), []));
        }

        items.Add(new("addon", BulbFilter.AddOn, "Add-On Bulbs", Count(BulbFilter.AddOn), []));
        items.Add(new(ShowOption.MyBulbsKey, BulbFilter.MyBulbs, "My Bulbs", Count(BulbFilter.MyBulbs), []));
        IEnumerable<ShowOption> categories = addOnsOnly
            ? catalog.GetAllCategoryNames().Select(c => new ShowOption(CategoryKey(c), BulbFilter.Category, c, Count(BulbFilter.Category, [c]), [c]))
                .Where(o => o.Count > 0)
            : catalog.GetCategoryCounts().Where(c => c.Count > 0).Select(c => new ShowOption(CategoryKey(c.Name), BulbFilter.Category, c.Name, c.Count, [c.Name]));
        List<ShowOption> categoryItems = [.. categories.OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)];
        if (categoryItems.Count > 0)
        {
            items.Add(null);
            items.AddRange(categoryItems);
        }

        int removed = Count(BulbFilter.Removed);
        if (removed > 0)
        {
            items.Add(new(ShowOption.RemovedKey, BulbFilter.Removed, "Removed Bulbs", removed, []));
        }

        return items;
    }

    /// <summary>The key of a category item (case-insensitive).</summary>
    /// <param name="category">The category.</param>
    /// <returns>The key.</returns>
    public static string CategoryKey(string category) => "category:" + category.ToUpperInvariant();

    /// <summary>The bulbs on the screen (every edge and corner).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <returns>The ids.</returns>
    public static IReadOnlySet<string> InUse(SlotAssignment arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        var ids = new HashSet<string>(BulbIds.Comparer);
        foreach (Side side in CellSlots.Sides)
        {
            ids.UnionWith(arrangement.GetEdge(side));
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            if (arrangement.GetCorner(corner) is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>The result line (a polite live region): "1,550 bulbs", "37 bulbs match "snow"", or the indexing progress.</summary>
    /// <param name="count">The number of bulbs listed.</param>
    /// <param name="search">The search text.</param>
    /// <param name="status">The catalog status.</param>
    /// <returns>The text.</returns>
    public static string ResultLine(int count, string search, BulbCatalogStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!status.IsComplete)
        {
            return string.Create(CultureInfo.CurrentCulture, $"Loading add-on bulbs… {status.IndexedAddOns:N0} of {status.TotalAddOns:N0}");
        }

        string bulbs = count == 1 ? "1 bulb" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} bulbs");
        string trimmed = search.Trim();
        return trimmed.Length == 0 ? bulbs : $"{bulbs} {(count == 1 ? "matches" : "match")} \"{trimmed}\"";
    }

    /// <summary>The empty-state sentence of a filter (PRODUCT-SPEC 3.2.9).</summary>
    /// <param name="option">The Show item.</param>
    /// <param name="search">The search text.</param>
    /// <returns>The sentence.</returns>
    public static string EmptyText(ShowOption option, string search)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (search.Trim().Length > 0)
        {
            return $"No bulbs match \"{search.Trim()}\".";
        }

        return option.Key switch
        {
            ShowOption.FavoritesKey => "No favorites yet. Click the star on any bulb to keep it here.",
            ShowOption.MyBulbsKey => "Bulbs you add or make appear here. Click Add Bulb… to add a .bul file or to turn an animated GIF into a bulb.",
            ShowOption.InUseKey => "No bulbs are on your screen. Double-click a bulb, or drag bulbs into the boxes.",
            ShowOption.RemovedKey => "No removed bulbs.",
            _ => "No bulbs here.",
        };
    }

    /// <summary>The placeholder of the search box: "Search 1,550 bulbs" (live count).</summary>
    /// <param name="total">The number of bulbs.</param>
    /// <returns>The placeholder.</returns>
    public static string Placeholder(int total) => string.Create(CultureInfo.CurrentCulture, $"Search {total:N0} bulbs");
}
