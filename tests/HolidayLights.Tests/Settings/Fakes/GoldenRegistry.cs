using System.Globalization;
using System.Text.Json;
using HolidayLights.Core.Legacy;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings.Fakes;

/// <summary>
/// The commissioning PC's 5.4 registry (golden <c>legacy-registry.json</c>, a decoded 5.4 registry
/// with the user's paths replaced by placeholders) as a snapshot, and the raw installer themes of <c>default-themes.json</c>.
/// </summary>
internal static class GoldenRegistry
{
    /// <summary>Reads the golden registry.</summary>
    /// <param name="programFolder">The 5.4 program folder to report (the golden <c>Path</c> names this PC's real folder, which tests never read).</param>
    /// <returns>The snapshot.</returns>
    public static LegacyRegistrySnapshot Load(string? programFolder = null)
    {
        using JsonDocument golden = GoldenData.ReadJson("legacy-registry.json");
        JsonElement root = golden.RootElement;
        return new LegacyRegistrySnapshot
        {
            Values = ReadKey(root.GetProperty("HKCU main")),
            Themes = [.. root.GetProperty("HKCU Themes").EnumerateObject().Select(t => new KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>(t.Name, ReadKey(t.Value)))],
            IncludedBulbCategories = root.GetProperty("HKCU Included Bulb Categories").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase),
            MachinePath = root.GetProperty("HKLM (32-bit view = WOW6432Node)").GetProperty("Path").GetProperty("raw").GetString(),
            ProgramFolder = programFolder,
        };
    }

    /// <summary>The raw registry values of the 11 installer themes (golden <c>default-themes.json</c>).</summary>
    /// <returns>Theme name to values.</returns>
    public static IReadOnlyList<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>> InstallerThemes()
    {
        using JsonDocument golden = GoldenData.ReadJson("default-themes.json");
        var themes = new List<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>>();
        foreach (JsonProperty theme in golden.RootElement.GetProperty("themes").EnumerateObject())
        {
            var values = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty value in theme.Value.EnumerateObject())
            {
                JsonElement e = value.Value;
                values[value.Name] = e.GetProperty("regType").GetString() switch
                {
                    "REG_SZ" => LegacyValue.OfString(e.GetProperty("raw").GetString()!),
                    "REG_DWORD" => LegacyValue.OfDWord(e.GetProperty("raw").GetUInt32()),
                    _ => LegacyValue.OfBinary(Convert.FromHexString(e.GetProperty("rawHex").GetString()!)),
                };
            }

            themes.Add(new(theme.Name, values));
        }

        return themes;
    }

    private static Dictionary<string, LegacyValue> ReadKey(JsonElement key)
    {
        var values = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty value in key.EnumerateObject())
        {
            JsonElement e = value.Value;
            values[value.Name] = e.GetProperty("type").GetString() switch
            {
                "REG_SZ" => LegacyValue.OfString(e.GetProperty("raw").GetString()!),
                "REG_DWORD" => LegacyValue.OfDWord(uint.Parse(e.GetProperty("raw").GetString()![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                _ => LegacyValue.OfBinary(Convert.FromHexString(e.GetProperty("raw_hex").GetString()!)),
            };
        }

        return values;
    }
}
