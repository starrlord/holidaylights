namespace HolidayLights.Core.Legacy;

/// <summary>Turns the names and numbers 5.4 stored into 6.0 ids.</summary>
/// <param name="Bulb">A "Bulb Settings" id to a bulb id (null when the bulb is not installed).</param>
/// <param name="AddOnByName">An add-on bulb's name (a "Screen Saver Module" value) to its bulb id, or null.</param>
/// <param name="Song">A song file name of "Holiday Lights Music" to a song id, or null when the song is not available.</param>
/// <param name="Picture">A picture file name of "Holiday Lights Pictures" to a picture id, or null when not available.</param>
internal sealed record LegacyResolver(
    Func<int, string?> Bulb,
    Func<string, string?> AddOnByName,
    Func<string, string?> Song,
    Func<string, string?> Picture)
{
    /// <summary>
    /// The resolver that compares 5.4 values with the shipped themes independently of the installed files: built-in ids by
    /// the built-in table, no add-ons, every song and picture name as the bundled file of that name.
    /// </summary>
    public static LegacyResolver ForComparison { get; } = new(
        LegacyBuiltInBulbs.ById,
        _ => null,
        name => MediaIds.Bundled(name),
        name => MediaIds.Bundled(name));
}
