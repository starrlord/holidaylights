namespace HolidayLights.Core.Legacy;

/// <summary>Kinds of raw registry values of 5.4.</summary>
public enum LegacyValueKind
{
    /// <summary>REG_SZ.</summary>
    String,

    /// <summary>REG_DWORD.</summary>
    DWord,

    /// <summary>REG_BINARY.</summary>
    Binary,
}

/// <summary>One raw 5.4 registry value, exactly as stored.</summary>
/// <param name="Kind">The registry type.</param>
/// <param name="Text">REG_SZ text (ANSI decoded), else null.</param>
/// <param name="Number">REG_DWORD value, else null.</param>
/// <param name="Bytes">REG_BINARY bytes, else null.</param>
public sealed record LegacyValue(LegacyValueKind Kind, string? Text, uint? Number, byte[]? Bytes)
{
    /// <summary>A REG_SZ value.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    public static LegacyValue OfString(string text) => new(LegacyValueKind.String, text, null, null);

    /// <summary>A REG_DWORD value.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The value.</returns>
    public static LegacyValue OfDWord(uint number) => new(LegacyValueKind.DWord, null, number, null);

    /// <summary>A REG_BINARY value.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The value.</returns>
    public static LegacyValue OfBinary(byte[] bytes) => new(LegacyValueKind.Binary, null, null, bytes);
}

/// <summary>
/// Everything the import reads from the registry, without interpretation: <c>HKCU\Software\Tiger Technologies\Holiday
/// Lights</c> with its <c>Themes</c> and <c>Included Bulb Categories</c> subkeys, plus the HKLM <c>Path</c>.
/// Golden <c>legacy-registry.json</c> is a decoded 5.4 registry. Owner: core-settings.
/// </summary>
public sealed record LegacyRegistrySnapshot
{
    /// <summary>Values of the main key by name (e.g. "Bulb Settings", "Flash Interval", "Custom Color  0").</summary>
    public IReadOnlyDictionary<string, LegacyValue> Values { get; init; } = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Each subkey of <c>Themes</c> (in registry enumeration order) with its values. A theme whose name held a "\" is a
    /// nested key; its name joins the key names with "\".
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>> Themes { get; init; } = [];

    /// <summary><c>Included Bulb Categories</c>: built-in bulb name to the "Cat|Cat" string.</summary>
    public IReadOnlyDictionary<string, string> IncludedBulbCategories { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The HKLM <c>Path</c> (32-bit view), the fallback for the 5.4 folder, or null.</summary>
    public string? MachinePath { get; init; }

    /// <summary>The user's Startup folder shortcuts whose name matches <c>*Holiday Lights*.lnk</c>, with their targets.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> StartupShortcuts { get; init; } = [];

    /// <summary>
    /// The 5.4 program folder (PRODUCT-SPEC 6.8.1): the folder of the HKCU <c>Path</c>, else of <see cref="MachinePath"/>,
    /// expanded to its long form (<c>GetLongPathName</c>; for example <c>C:\Program Files\Holiday Lights</c>); null when neither
    /// names an existing folder. Its <c>Holiday Lights Bulbs</c>, <c>Music</c> and <c>Pictures</c> folders are imported.
    /// </summary>
    public string? ProgramFolder { get; init; }
}

/// <summary>Reads the 5.4 registry, read-only. Implemented in <c>Platform\Legacy\</c> (core-settings).</summary>
public interface ILegacyRegistrySource
{
    /// <summary>Reads everything the import needs.</summary>
    /// <returns>The snapshot, or null when <c>HKCU\Software\Tiger Technologies\Holiday Lights</c> does not exist.</returns>
    LegacyRegistrySnapshot? Read();
}
