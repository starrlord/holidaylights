namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// Reads bulb ids from settings defensively: the settings file can be edited by hand, so empty ids are skipped and
/// overrides whose ids differ only in case collapse to one (the last one wins).
/// </summary>
internal static class PreferenceIds
{
    /// <summary>The non-empty ids of a list.</summary>
    /// <param name="ids">Ids from settings (may hold nulls).</param>
    /// <returns>The usable ids.</returns>
    public static IEnumerable<string> Of(IEnumerable<string?>? ids) =>
        ids?.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!) ?? [];

    /// <summary>Category overrides keyed case-insensitively, without empty ids, missing lists or empty names.</summary>
    /// <param name="overrides">Overrides from settings.</param>
    /// <returns>A new dictionary.</returns>
    public static Dictionary<string, IReadOnlyList<string>> Overrides(IReadOnlyDictionary<string, IReadOnlyList<string>>? overrides)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(BulbIds.Comparer);
        foreach ((string id, IReadOnlyList<string>? categories) in overrides ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            if (!string.IsNullOrEmpty(id) && categories is not null)
            {
                result[id] = [.. categories.Where(c => !string.IsNullOrEmpty(c))];
            }
        }

        return result;
    }
}
