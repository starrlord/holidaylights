using System.Globalization;

namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// An immutable view of the catalog at one moment: every known bulb in the 5.4 list order with the user's favorites,
/// hidden bulbs and category overrides applied. Queries run on it from any thread.
/// </summary>
internal sealed class CatalogSnapshot
{
    private readonly IReadOnlyList<BulbInfo> ordered;
    private readonly Dictionary<string, BulbInfo> byId;
    private readonly Dictionary<string, SearchText> searchText;
    private readonly Dictionary<string, string[]> foldedCategories;
    private readonly HashSet<string> hidden;
    private readonly HashSet<string> favorites;
    private readonly Dictionary<string, string> byIdentity;
    private readonly Lazy<IReadOnlyList<BulbCategoryCount>> categoryCounts;
    private readonly Lazy<IReadOnlyList<string>> categoryNames;

    private CatalogSnapshot(
        IReadOnlyList<CatalogEntry> entries, BulbPreferences preferences, IReadOnlyList<DamagedBulbFile> damaged, BulbCatalogStatus status)
    {
        hidden = new HashSet<string>(PreferenceIds.Of(preferences.Hidden), BulbIds.Comparer);
        favorites = new HashSet<string>(PreferenceIds.Of(preferences.Favorites), BulbIds.Comparer);
        Dictionary<string, IReadOnlyList<string>> overrides = PreferenceIds.Overrides(preferences.CategoryOverrides);
        byId = new Dictionary<string, BulbInfo>(entries.Count, BulbIds.Comparer);
        searchText = new Dictionary<string, SearchText>(entries.Count, BulbIds.Comparer);
        foldedCategories = new Dictionary<string, string[]>(entries.Count, BulbIds.Comparer);
        byIdentity = new Dictionary<string, string>(StringComparer.Ordinal);
        var all = new List<BulbInfo>(entries.Count);
        var folded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CatalogEntry entry in entries.OrderBy(e => e, OriginalOrderComparer.Instance))
        {
            BulbInfo info = ToInfo(entry, overrides.GetValueOrDefault(entry.Id) ?? entry.Categories, all.Count);
            all.Add(info);
            byId[entry.Id] = info;
            searchText[entry.Id] = entry.Search;
            foldedCategories[entry.Id] = [.. info.Categories.Select(c => folded.TryGetValue(c, out string? f) ? f : folded[c] = SearchText.Fold(c))];
        }

        // Bundled bulbs win over My Bulbs with the same content (the 5.4 import maps to addon: ids).
        foreach (CatalogEntry entry in entries.Where(e => e.Identity is not null).OrderBy(e => e.Origin == BulbOrigin.BundledAddOn ? 0 : 1))
        {
            byIdentity.TryAdd(entry.Identity!, entry.Id);
        }

        ordered = all;
        Listed = [.. all.Where(b => !hidden.Contains(b.Id))];
        Damaged = damaged;
        Status = status;
        categoryCounts = new Lazy<IReadOnlyList<BulbCategoryCount>>(CountListedCategories);
        categoryNames = new Lazy<IReadOnlyList<string>>(() => NameAllCategories(overrides));
    }

    /// <summary>An empty catalog.</summary>
    public static CatalogSnapshot Empty { get; } = new([], new BulbPreferences(), [], new BulbCatalogStatus(0, 0, false));

    /// <summary>Every listed bulb (not hidden), in original order.</summary>
    public IReadOnlyList<BulbInfo> Listed { get; }

    /// <summary>Files that could not be loaded.</summary>
    public IReadOnlyList<DamagedBulbFile> Damaged { get; }

    /// <summary>Indexing progress.</summary>
    public BulbCatalogStatus Status { get; }

    /// <summary>Non-empty categories of listed bulbs with counts, A-Z.</summary>
    public IReadOnlyList<BulbCategoryCount> CategoryCounts => categoryCounts.Value;

    /// <summary>Every known category name, A-Z.</summary>
    public IReadOnlyList<string> CategoryNames => categoryNames.Value;

    /// <summary>Creates a snapshot.</summary>
    /// <param name="entries">Every known bulb (hidden ones included, damaged files excluded).</param>
    /// <param name="preferences">Favorites, hidden bulbs and category overrides.</param>
    /// <param name="damaged">Files that could not be loaded.</param>
    /// <param name="status">Indexing progress.</param>
    /// <returns>The snapshot.</returns>
    public static CatalogSnapshot Build(
        IReadOnlyList<CatalogEntry> entries, BulbPreferences preferences, IReadOnlyList<DamagedBulbFile> damaged, BulbCatalogStatus status) =>
        new(entries, preferences, damaged, status);

    /// <summary>Finds the metadata of a known bulb (hidden ones included).</summary>
    /// <param name="id">The id.</param>
    /// <param name="info">The metadata.</param>
    /// <returns>True when known.</returns>
    public bool TryGetInfo(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BulbInfo? info) =>
        byId.TryGetValue(id, out info);

    /// <summary>Finds the bulb with a content identity (a bundled bulb before a My Bulbs file).</summary>
    /// <param name="identity">A <see cref="BulFile.ComputeContentIdentity"/> key.</param>
    /// <returns>The id, or null.</returns>
    public string? FindByIdentity(string identity) => byIdentity.GetValueOrDefault(identity);

    /// <summary>Filters, searches and sorts.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The bulbs in display order.</returns>
    public IReadOnlyList<BulbInfo> Query(BulbQuery query)
    {
        IEnumerable<BulbInfo> bulbs = Filter(query);
        if (query.AddOnsOnly)
        {
            bulbs = bulbs.Where(b => b.Origin != BulbOrigin.BuiltIn);
        }

        IComparer<BulbInfo> sort = SortComparer.For(query.Sort);
        if (SearchQuery.Parse(query.SearchText) is not { } search)
        {
            return [.. bulbs.OrderBy(b => b, sort)];
        }

        return [.. bulbs
            .Select(b => (Bulb: b, Rank: search.Rank(searchText[b.Id], foldedCategories[b.Id])))
            .Where(r => r.Rank != SearchQuery.NoMatch)
            .OrderBy(r => r.Rank)
            .ThenBy(r => r.Bulb, sort)
            .Select(r => r.Bulb)];
    }

    private IEnumerable<BulbInfo> Filter(BulbQuery query)
    {
        switch (query.Filter)
        {
            case BulbFilter.Favorites:
                return Listed.Where(b => favorites.Contains(b.Id));
            case BulbFilter.InUse:
            {
                var inUse = new HashSet<string>(query.InUseIds ?? (IEnumerable<string>)[], BulbIds.Comparer);
                return Listed.Where(b => inUse.Contains(b.Id));
            }

            case BulbFilter.Holiday:
                return Listed.Where(b => b.Categories.Any(c => query.Categories.Contains(c, StringComparer.OrdinalIgnoreCase)));
            case BulbFilter.Category:
            {
                string? category = query.Categories.FirstOrDefault();
                return Listed.Where(b => category is not null && b.Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
            }

            case BulbFilter.BuiltIn:
                return Listed.Where(b => b.Origin == BulbOrigin.BuiltIn);
            case BulbFilter.AddOn:
                return Listed.Where(b => b.Origin != BulbOrigin.BuiltIn);
            case BulbFilter.MyBulbs:
                return Listed.Where(b => b.Origin == BulbOrigin.UserAddOn);
            case BulbFilter.Removed:
                return ordered.Where(b => hidden.Contains(b.Id));
            default:
                return Listed;
        }
    }

    private IReadOnlyList<BulbCategoryCount> CountListedCategories()
    {
        var tally = new CategoryTally();
        foreach (BulbInfo bulb in Listed)
        {
            tally.Add(bulb.Id, bulb.Categories);
        }

        return tally.ToCounts();
    }

    private IReadOnlyList<string> NameAllCategories(IReadOnlyDictionary<string, IReadOnlyList<string>> overrides)
    {
        var tally = new CategoryTally();
        foreach (BulbInfo bulb in ordered)
        {
            tally.Add(bulb.Id, bulb.OriginalCategories);
        }

        foreach ((string id, IReadOnlyList<string> categories) in overrides)
        {
            tally.Add(id, categories);
        }

        return tally.ToNames();
    }

    private static BulbInfo ToInfo(CatalogEntry entry, IReadOnlyList<string> categories, int order) => new()
    {
        Id = entry.Id,
        Origin = entry.Origin,
        Name = entry.Name,
        Description = entry.Description,
        Author = entry.Author,
        Copyright = entry.Copyright,
        Categories = categories,
        OriginalCategories = entry.Categories,
        FilePath = entry.FilePath,
        FileDate = entry.LastWriteUtc is { } written ? new DateTimeOffset(DateTime.SpecifyKind(written, DateTimeKind.Utc)) : null,
        AddedDate = entry.Origin == BulbOrigin.UserAddOn && entry.CreatedUtc is { } created
            ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc))
            : null,
        OriginalOrder = order,
        TopFlavorCount = entry.TopFlavorCount,
        TopKind = entry.TopKind,
        LargestTopCell = entry.LargestTopCell,
        IsBig = entry.IsBig,
        HasDamagedArt = entry.HasDamagedArt,
        IsEditable = entry.IsEditable,
    };

    /// <summary>
    /// "Original Order": the built-ins in table order, then add-ons sorted like 5.4 sorted its list by name; equal names by
    /// file name, bundled first.
    /// </summary>
    private sealed class OriginalOrderComparer : IComparer<CatalogEntry>
    {
        public static OriginalOrderComparer Instance { get; } = new();

        public int Compare(CatalogEntry? x, CatalogEntry? y)
        {
            if (x is null || y is null)
            {
                return x is null ? (y is null ? 0 : -1) : 1;
            }

            bool xBuiltIn = x.Origin == BulbOrigin.BuiltIn;
            bool yBuiltIn = y.Origin == BulbOrigin.BuiltIn;
            if (xBuiltIn || yBuiltIn)
            {
                return xBuiltIn && yBuiltIn ? x.TableIndex.CompareTo(y.TableIndex) : xBuiltIn ? -1 : 1;
            }

            int result = LegacyNameComparer.Instance.Compare(x.Name, y.Name);
            if (result == 0)
            {
                result = StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(x.FilePath), Path.GetFileName(y.FilePath));
            }

            if (result == 0)
            {
                result = x.Origin.CompareTo(y.Origin);
            }

            return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id);
        }
    }

    /// <summary>The Sort choices of the Bulb List; ties keep the original order.</summary>
    private sealed class SortComparer(BulbSortOrder sort) : IComparer<BulbInfo>
    {
        private static readonly SortComparer Original = new(BulbSortOrder.Original);

        public static IComparer<BulbInfo> For(BulbSortOrder sort) => sort == BulbSortOrder.Original ? Original : new SortComparer(sort);

        public int Compare(BulbInfo? x, BulbInfo? y)
        {
            if (x is null || y is null)
            {
                return x is null ? (y is null ? 0 : -1) : 1;
            }

            int result = sort switch
            {
                BulbSortOrder.Name => string.Compare(x.Name, y.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase),
                BulbSortOrder.Newest => CompareNewestFirst(x.FileDate, y.FileDate),
                _ => 0,
            };
            return result != 0 ? result : x.OriginalOrder.CompareTo(y.OriginalOrder);
        }

        private static int CompareNewestFirst(DateTimeOffset? x, DateTimeOffset? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => y.Value.CompareTo(x.Value),
        };
    }
}
