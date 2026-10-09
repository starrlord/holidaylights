namespace HolidayLights.Core.Bulbs;

/// <summary>
/// The 49 built-in bulbs from the embedded table (<c>assets/builtin/table.json</c>) and RT_BITMAP art, sliced exactly as
/// 5.4 does (golden <c>builtin-cells.json</c>). Owner: core-bulbs.
/// </summary>
public static class BuiltInBulbs
{
    private static readonly Lazy<IReadOnlyList<BuiltInBulb>> Bulbs =
        new(() => [.. BuiltInTable.Load().Bulbs.OrderBy(r => r.TableIndex).Select(r => new BuiltInBulb(r))]);

    private static readonly Lazy<IReadOnlyDictionary<string, BuiltInBulb>> ById =
        new(() => Bulbs.Value.ToDictionary(b => b.Id, BulbIds.Comparer));

    private static readonly Lazy<IReadOnlyDictionary<int, BuiltInBulb>> ByLegacyId =
        new(() => Bulbs.Value.GroupBy(b => b.LegacyId).ToDictionary(g => g.Key, g => g.First()));

    /// <summary>Loads the 49 built-in bulbs in table order (cells decode lazily). The result is cached.</summary>
    /// <returns>The bulbs; <see cref="IBulb.LegacyId"/> is the record id (not the table index).</returns>
    public static IReadOnlyList<IBulb> Load() => Bulbs.Value;

    /// <summary>
    /// A cell of a built-in bulb's sheet by its 1-based cell number over the normal rows 0-3 (the 5.4
    /// <c>IncludedBulbObject_CellIndexToRect</c> walk), as the screen saver's
    /// picture animations address the sheets, independently of the edge tables.
    /// </summary>
    /// <param name="legacyId">The 5.4 record id of the bulb (<see cref="IBulb.LegacyId"/>).</param>
    /// <param name="cell">The 1-based cell number.</param>
    /// <returns>
    /// The cell (shared and read-only) with its sheet rectangle, or null where 5.4 throws (unknown id, cell 0, or past the
    /// last normal row).
    /// </returns>
    public static BulbCell? GetSheetCell(int legacyId, int cell) =>
        ByLegacyId.Value.TryGetValue(legacyId, out BuiltInBulb? bulb) ? bulb.GetSheetCell(cell) : null;

    /// <summary>Finds a built-in bulb by id.</summary>
    /// <param name="id">A <c>builtin:</c> id (compared with <see cref="BulbIds.Comparer"/>).</param>
    /// <param name="bulb">The bulb when found.</param>
    /// <returns>True when the id names a built-in bulb.</returns>
    internal static bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BuiltInBulb? bulb) =>
        ById.Value.TryGetValue(id, out bulb);

    /// <summary>The built-in bulbs with their table data, in table order.</summary>
    internal static IReadOnlyList<BuiltInBulb> All => Bulbs.Value;
}
