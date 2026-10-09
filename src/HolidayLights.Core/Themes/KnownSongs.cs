namespace HolidayLights.Core.Themes;

/// <summary>The songs the Music Box knows, in its order, for converting between checked and unchecked songs.</summary>
internal sealed class KnownSongs
{
    private readonly HashSet<string> set;

    /// <summary>Creates the set.</summary>
    /// <param name="ids">Song ids in Music Box order.</param>
    public KnownSongs(IEnumerable<string> ids)
    {
        set = new HashSet<string>(MediaIds.Comparer);
        Ordered = [.. ids.Where(set.Add)];
    }

    /// <summary>The ids in Music Box order.</summary>
    public IReadOnlyList<string> Ordered { get; }

    /// <summary>Every song of the library, hidden ones included.</summary>
    /// <param name="songs">The song library.</param>
    /// <returns>The known songs.</returns>
    public static KnownSongs From(ISongLibrary songs) => new(songs.Songs.Concat(songs.HiddenSongs).Select(s => s.Id));

    /// <summary>True for a known song.</summary>
    /// <param name="id">A song id.</param>
    /// <returns>True when known.</returns>
    public bool Contains(string id) => set.Contains(id);

    /// <summary>The checked songs of a theme (a theme stores its checked songs; null = every song).</summary>
    /// <param name="enabled">The theme's checked songs.</param>
    /// <returns>The known songs that are checked.</returns>
    public HashSet<string> CheckedOfTheme(IReadOnlyList<string>? enabled) =>
        enabled is null ? new HashSet<string>(set, MediaIds.Comparer) : new HashSet<string>(enabled.Where(Contains), MediaIds.Comparer);

    /// <summary>The checked songs of settings (settings store the unchecked songs).</summary>
    /// <param name="disabled">The unchecked songs.</param>
    /// <returns>The known songs that are checked.</returns>
    public HashSet<string> CheckedOfSettings(IReadOnlyList<string> disabled)
    {
        var result = new HashSet<string>(set, MediaIds.Comparer);
        result.ExceptWith(disabled);
        return result;
    }
}
