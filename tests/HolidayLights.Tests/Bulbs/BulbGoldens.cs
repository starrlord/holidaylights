using System.Text.Json;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

/// <summary>Access to the bulb goldens and the bundled bulb files shared by the tests of this folder.</summary>
internal static class BulbGoldens
{
    private static readonly Lazy<JsonDocument> BuiltInCellsDocument = new(() => GoldenData.ReadJson("builtin-cells.json"));
    private static readonly Lazy<JsonDocument> BulFramesDocument = new(() => GoldenData.ReadJson("bul-frames.json.gz"));

    /// <summary>The folder of the 1,501 bundled <c>.bul</c> files in the test output.</summary>
    public static string BundledBulbsFolder => Path.Combine(TestPaths.ContentFolder, "Bulbs");

    /// <summary><c>builtin-cells.json</c>: <c>bulbs.&lt;slug&gt;</c> records.</summary>
    public static JsonElement BuiltInCells => BuiltInCellsDocument.Value.RootElement.GetProperty("bulbs");

    /// <summary><c>bul-frames.json.gz</c>: one record per bundled file, sorted by file name.</summary>
    public static IReadOnlyList<JsonElement> BulFiles => [.. BulFramesDocument.Value.RootElement.GetProperty("files").EnumerateArray()];

    /// <summary>Reads a bundled bulb file.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ReadBundled(string fileName) => File.ReadAllBytes(Path.Combine(BundledBulbsFolder, fileName));

    /// <summary>Reads a string array property.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The strings.</returns>
    public static string[] Strings(JsonElement element, string name) =>
        [.. element.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];

    /// <summary>Reads an int array.</summary>
    /// <param name="element">The array.</param>
    /// <returns>The values.</returns>
    public static int[] Ints(JsonElement element) => [.. element.EnumerateArray().Select(e => e.GetInt32())];
}
