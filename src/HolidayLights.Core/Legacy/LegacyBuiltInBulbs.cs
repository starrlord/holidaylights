using System.Text.Json;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The 49 built-in bulbs by their 5.4 numeric id (0-48, the record id, not the table index) and name, read from the
/// embedded built-in table (<c>assets/builtin/table.json</c>): "Bulb Settings" ids map to <c>builtin:&lt;slug&gt;</c>
/// by id, "Included Bulb Categories" names by name.
/// </summary>
internal static class LegacyBuiltInBulbs
{
    private static readonly Lazy<IReadOnlyList<(int Id, string BulbId, string Name)>> Table = new(Load);

    /// <summary>The bulb id of a built-in 5.4 id.</summary>
    /// <param name="legacyId">A 5.4 id.</param>
    /// <returns><c>builtin:&lt;slug&gt;</c>, or null when the id is not a built-in bulb.</returns>
    public static string? ById(int legacyId) => Table.Value.FirstOrDefault(b => b.Id == legacyId).BulbId;

    /// <summary>The bulb id of a built-in bulb name (as 5.4 named its "Included Bulb Categories" values).</summary>
    /// <param name="name">The bulb name (case-insensitive).</param>
    /// <returns><c>builtin:&lt;slug&gt;</c>, or null.</returns>
    public static string? ByName(string name) =>
        Table.Value.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)).BulbId;

    /// <summary>True for the ids the built-in bulbs hold (an add-on's id is bumped past them).</summary>
    /// <param name="legacyId">A 5.4 id.</param>
    /// <returns>True when taken by a built-in bulb.</returns>
    public static bool IsBuiltInId(int legacyId) => Table.Value.Any(b => b.Id == legacyId);

    private static IReadOnlyList<(int Id, string BulbId, string Name)> Load()
    {
        using Stream stream = EmbeddedAssets.Open(EmbeddedAssets.BuiltInTable);
        using JsonDocument table = JsonDocument.Parse(stream);
        return
        [
            .. table.RootElement.GetProperty("bulbs").EnumerateArray().Select(bulb => (
                bulb.GetProperty("id").GetInt32(),
                BulbIds.BuiltIn(bulb.GetProperty("slug").GetString()!),
                bulb.GetProperty("name").GetString()!)),
        ];
    }
}
