using System.Globalization;

namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// Merges category names case-insensitively (built-in, bundled, user and override categories), counts the bulbs in each
/// and picks the display spelling: the one most bulbs use, then the first in ordinal order.
/// </summary>
internal sealed class CategoryTally
{
    private readonly Dictionary<string, Tally> tallies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds the categories of one bulb (a category listed twice counts once).</summary>
    /// <param name="bulbId">The bulb.</param>
    /// <param name="categories">Its categories.</param>
    public void Add(string bulbId, IEnumerable<string> categories)
    {
        foreach (string category in categories)
        {
            if (category.Length == 0)
            {
                continue;
            }

            if (!tallies.TryGetValue(category, out Tally? tally))
            {
                tally = new Tally();
                tallies[category] = tally;
            }

            if (tally.Bulbs.Add(bulbId))
            {
                tally.Spellings[category] = tally.Spellings.GetValueOrDefault(category) + 1;
            }
        }
    }

    /// <summary>The categories with their bulb counts, A-Z.</summary>
    /// <returns>The counts.</returns>
    public IReadOnlyList<BulbCategoryCount> ToCounts() =>
        [.. Sorted().Select(t => new BulbCategoryCount(t.Name, t.Tally.Bulbs.Count))];

    /// <summary>The category names, A-Z.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> ToNames() => [.. Sorted().Select(t => t.Name)];

    private IEnumerable<(string Name, Tally Tally)> Sorted()
    {
        var culture = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        return tallies.Values
            .Select(t => (Name: t.DisplayName(), Tally: t))
            .OrderBy(t => t.Name, culture)
            .ThenBy(t => t.Name, StringComparer.Ordinal);
    }

    private sealed class Tally
    {
        public HashSet<string> Bulbs { get; } = new(BulbIds.Comparer);

        public Dictionary<string, int> Spellings { get; } = new(StringComparer.Ordinal);

        public string DisplayName() =>
            Spellings.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key;
    }
}
