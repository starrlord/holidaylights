using System.IO;
using System.Text.Json;

namespace HolidayLights.App.ScreenSaver.Art;

/// <summary>One floater record of a built-in animation: a built-in bulb and the sheet cells it cycles through.</summary>
/// <param name="BulbId">The 5.4 bulb id (record id).</param>
/// <param name="Cells">The 1-based cells of the frames, in order.</param>
internal sealed record SaverAnimationRecord(int BulbId, IReadOnlyList<int> Cells);

/// <summary>One of the 21 picture animations (the 5.4 animation table).</summary>
/// <param name="Name">The animation name ("Angel", "Baubles", ...).</param>
/// <param name="GifResource">The RCDATA id of a GIF animation (3100-3105), or null.</param>
/// <param name="Records">The built-in floater records (empty for a GIF animation).</param>
internal sealed record SaverAnimationEntry(string Name, int? GifResource, IReadOnlyList<SaverAnimationRecord> Records);

/// <summary>The 21-entry animation table (<c>assets/heritage/saver/anim_table.json</c>).</summary>
internal static class SaverAnimationTable
{
    private static readonly Lazy<IReadOnlyDictionary<string, SaverAnimationEntry>> Entries = new(Load);

    /// <summary>The entries by exact name (5.4 compared with <c>strcmp</c>).</summary>
    public static IReadOnlyDictionary<string, SaverAnimationEntry> All => Entries.Value;

    /// <summary>Finds an animation.</summary>
    /// <param name="name">The exact name.</param>
    /// <param name="entry">The entry when found.</param>
    /// <returns>True for one of the 21 names.</returns>
    public static bool TryGet(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaverAnimationEntry? entry) =>
        Entries.Value.TryGetValue(name, out entry);

    private static Dictionary<string, SaverAnimationEntry> Load()
    {
        using JsonDocument document = JsonDocument.Parse(EmbeddedAssets.ReadAllBytes(HeritageAssets.SaverAnimationTable));
        var entries = new Dictionary<string, SaverAnimationEntry>(StringComparer.Ordinal);
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            string name = item.GetProperty("name").GetString() ?? "";
            int count = item.GetProperty("count").GetInt32();
            SaverAnimationRecord[] records = count < 1
                ? []
                : [.. item.GetProperty("records").EnumerateArray().Select(ReadRecord)];
            entries[name] = new SaverAnimationEntry(name, count < 1 ? -count : null, records);
        }

        return entries.Count > 0 ? entries : throw new InvalidDataException("The screen saver animation table is empty.");
    }

    /// <summary>A record <c>{ short bulbId; short frameCount; short cells[8]; }</c>.</summary>
    private static SaverAnimationRecord ReadRecord(JsonElement record)
    {
        int[] values = [.. record.EnumerateArray().Select(v => v.GetInt32())];
        int frames = Math.Clamp(values[1], 1, values.Length - 2);
        return new SaverAnimationRecord(values[0], values[2..(2 + frames)]);
    }
}
