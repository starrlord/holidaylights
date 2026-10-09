using System.Buffers.Binary;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// Typed, 5.4-faithful access to the raw values of one registry key (the main key or a theme): names compare
/// case-insensitively, a DWORD may be stored as 4 binary bytes, and a string longer than 5.4's read buffer counts as
/// missing (<c>ERROR_MORE_DATA</c>).
/// </summary>
internal sealed class LegacyValueSet
{
    /// <summary>The read buffer of "Screen Saver Module" and "Screen Saver Picture Name" (MAX_PATH, NUL included).</summary>
    public const int PathBuffer = 260;

    /// <summary>The read buffer of "Screen Saver Message" (NUL included).</summary>
    public const int MessageBuffer = 256;

    private readonly Dictionary<string, LegacyValue> values;

    /// <summary>Wraps raw values.</summary>
    /// <param name="values">The values by name.</param>
    public LegacyValueSet(IEnumerable<KeyValuePair<string, LegacyValue>> values)
    {
        this.values = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, LegacyValue value) in values)
        {
            this.values.TryAdd(name, value);
        }
    }

    /// <summary>The value names.</summary>
    public IEnumerable<string> Names => values.Keys;

    /// <summary>True when the value exists.</summary>
    /// <param name="name">The value name.</param>
    /// <returns>True when present.</returns>
    public bool Contains(string name) => values.ContainsKey(name);

    /// <summary>Reads a DWORD (REG_DWORD, or REG_BINARY of exactly 4 bytes).</summary>
    /// <param name="name">The value name.</param>
    /// <returns>The number, or null when missing or of another type.</returns>
    public uint? Dword(string name) => values.GetValueOrDefault(name) switch
    {
        { Kind: LegacyValueKind.DWord, Number: { } number } => number,
        { Kind: LegacyValueKind.Binary, Bytes: { Length: 4 } bytes } => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        _ => null,
    };

    /// <summary>Reads a string that fits 5.4's read buffer.</summary>
    /// <param name="name">The value name.</param>
    /// <param name="buffer">5.4's buffer size in bytes, NUL included.</param>
    /// <returns>The text, or null when missing, too long or of another type.</returns>
    public string? Text(string name, int buffer) =>
        values.GetValueOrDefault(name) is { Kind: LegacyValueKind.String, Text: { } text } && text.Length < buffer ? text : null;

    /// <summary>Reads binary data.</summary>
    /// <param name="name">The value name.</param>
    /// <returns>The bytes, or null when missing or of another type.</returns>
    public byte[]? Bytes(string name) =>
        values.GetValueOrDefault(name) is { Kind: LegacyValueKind.Binary, Bytes: { } bytes } ? bytes : null;
}
