using HolidayLights.Core.Bulbs.Catalog;

namespace HolidayLights.Core.Bulbs;

/// <summary>Favorites, hidden bundled bulbs, category overrides and recently used bulbs (all kept in settings or memory, never in files).</summary>
public sealed partial class BulbCatalog
{
    private const int MaxRecentlyUsed = 20;

    private IReadOnlyList<string> recentlyUsed = [];

    /// <summary>
    /// Bulbs most recently put on the screen in this session, newest first (at most 20): every id that appears in the
    /// arrangement after a settings change that did not hold it before.
    /// </summary>
    public IReadOnlyList<string> RecentlyUsed => Volatile.Read(ref recentlyUsed);

    /// <inheritdoc />
    public bool IsFavorite(string id) => settings.Current.Bulbs.Favorites.Contains(id, BulbIds.Comparer);

    /// <inheritdoc />
    public void SetFavorite(string id, bool favorite)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        string description = favorite ? $"Add {NameOf(id)} to Favorites" : $"Remove {NameOf(id)} from Favorites";
        settings.Update(
            s =>
            {
                IReadOnlyList<string> favorites = s.Bulbs.Favorites;
                if (favorites.Contains(id, BulbIds.Comparer) == favorite)
                {
                    return s;
                }

                string[] changed = favorite ? [.. favorites, id] : [.. favorites.Where(f => !BulbIds.Comparer.Equals(f, id))];
                return s with { Bulbs = s.Bulbs with { Favorites = changed } };
            },
            SettingsChange.Edit(description));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Names are trimmed, "|" becomes a space (it separates categories in 5.4 files), empty and repeated names
    /// (ignoring case) are dropped. Choosing the bulb's own categories, in any order and case, removes the override.
    /// </remarks>
    public void SetCategoryOverrides(string id, IReadOnlyList<string> categories)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(categories);
        string[] chosen = [.. categories
            .Select(c => c.Replace('|', ' ').Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        IReadOnlyList<string>? own = TryGetInfo(id, out BulbInfo? info) ? info.OriginalCategories : null;
        bool removeOverride = own is not null && chosen.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(own);
        settings.Update(
            s =>
            {
                var overrides = s.Bulbs.CategoryOverrides
                    .Where(o => !BulbIds.Comparer.Equals(o.Key, id))
                    .ToDictionary(o => o.Key, o => o.Value);
                bool had = overrides.Count != s.Bulbs.CategoryOverrides.Count;
                if (!removeOverride)
                {
                    overrides[id] = chosen;
                }
                else if (!had)
                {
                    return s;
                }

                return s with { Bulbs = s.Bulbs with { CategoryOverrides = overrides } };
            },
            SettingsChange.Edit($"Change the categories of {NameOf(id)}"));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="id"/> is not an <c>addon:</c> id.</exception>
    public void Hide(string id)
    {
        RequireBundled(id);
        settings.Update(
            s => s.Bulbs.Hidden.Contains(id, BulbIds.Comparer)
                ? s
                : s with { Bulbs = s.Bulbs with { Hidden = [.. s.Bulbs.Hidden, id] } },
            SettingsChange.Edit($"Remove {NameOf(id)}"));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="id"/> is not an <c>addon:</c> id.</exception>
    public void Unhide(string id)
    {
        RequireBundled(id);
        settings.Update(
            s => s.Bulbs.Hidden.Contains(id, BulbIds.Comparer)
                ? s with { Bulbs = s.Bulbs with { Hidden = [.. s.Bulbs.Hidden.Where(h => !BulbIds.Comparer.Equals(h, id))] } }
                : s,
            SettingsChange.Edit($"Restore {NameOf(id)}"));
    }

    private static void RequireBundled(string id)
    {
        if (!BulbIds.TryGetOrigin(id, out BulbOrigin origin) || origin != BulbOrigin.BundledAddOn)
        {
            throw new ArgumentException($"'{id}' is not a bundled add-on bulb.", nameof(id));
        }
    }

    private string NameOf(string id) => TryGetInfo(id, out BulbInfo? info) ? info.Name : id;

    /// <summary>
    /// Follows every settings change (edits, Undo, Cancel, imports): favorites, hidden bulbs and overrides change what
    /// the catalog lists; the arrangement feeds <see cref="RecentlyUsed"/>.
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        TrackRecentlyUsed(e.OldSettings.Current.Arrangement, e.NewSettings.Current.Arrangement);
        BulbPreferences before = e.OldSettings.Bulbs;
        BulbPreferences after = e.NewSettings.Bulbs;
        if (ReferenceEquals(before, after))
        {
            return;
        }

        string[] hidden = Except(after.Hidden, before.Hidden);
        string[] updated = [.. Except(after.Favorites, before.Favorites)
            .Concat(Except(before.Favorites, after.Favorites))
            .Concat(Except(before.Hidden, after.Hidden))
            .Concat(ChangedOverrides(PreferenceIds.Overrides(before.CategoryOverrides), PreferenceIds.Overrides(after.CategoryOverrides)))
            .Distinct(BulbIds.Comparer)];
        if (hidden.Length == 0 && updated.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            PublishSnapshot();
        }

        if (hidden.Length > 0)
        {
            events.Post(BulbCatalogChange.Removed, hidden);
        }

        if (updated.Length > 0)
        {
            events.Post(BulbCatalogChange.Updated, updated);
        }
    }

    private void TrackRecentlyUsed(SlotAssignment before, SlotAssignment after)
    {
        if (before.Equals(after))
        {
            return;
        }

        var previous = new HashSet<string>(PreferenceIds.Of(before.EnumerateBulbIds()), BulbIds.Comparer);
        string[] added = [.. PreferenceIds.Of(after.EnumerateBulbIds()).Where(id => !previous.Contains(id)).Distinct(BulbIds.Comparer)];
        if (added.Length == 0)
        {
            return;
        }

        string[] list = [.. added.Concat(RecentlyUsed.Where(id => !added.Contains(id, BulbIds.Comparer))).Take(MaxRecentlyUsed)];
        Volatile.Write(ref recentlyUsed, list);
    }

    private static string[] Except(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        [.. PreferenceIds.Of(first).Except(PreferenceIds.Of(second), BulbIds.Comparer)];

    private static IEnumerable<string> ChangedOverrides(
        Dictionary<string, IReadOnlyList<string>> before, Dictionary<string, IReadOnlyList<string>> after)
    {
        foreach (string id in before.Keys.Union(after.Keys, BulbIds.Comparer))
        {
            if (!before.TryGetValue(id, out IReadOnlyList<string>? old) || !after.TryGetValue(id, out IReadOnlyList<string>? now)
                || !old.SequenceEqual(now, StringComparer.Ordinal))
            {
                yield return id;
            }
        }
    }
}
