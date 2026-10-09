using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>An opaque 24-bit colour, persisted as <c>"#RRGGBB"</c> (settings, themes, custom colours).</summary>
/// <param name="R">Red (0-255).</param>
/// <param name="G">Green (0-255).</param>
/// <param name="B">Blue (0-255).</param>
[JsonConverter(typeof(RgbColorJsonConverter))]
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>#000000 (5.4 default screen saver background and custom colours).</summary>
    public static RgbColor Black => new(0, 0, 0);

    /// <summary>#FFFFFF.</summary>
    public static RgbColor White => new(255, 255, 255);

    /// <summary>#FF0000 (5.4 default screen saver message colour).</summary>
    public static RgbColor Red => new(255, 0, 0);

    /// <summary>Parses <c>"#RRGGBB"</c> (hexadecimal, case-insensitive).</summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The colour.</returns>
    /// <exception cref="FormatException">The text is not <c>#RRGGBB</c>.</exception>
    public static RgbColor Parse(string text) =>
        TryParse(text, out RgbColor color) ? color : throw new FormatException($"'{text}' is not a #RRGGBB colour.");

    /// <summary>Tries to parse <c>"#RRGGBB"</c> (hexadecimal, case-insensitive).</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="color">The colour when parsing succeeded.</param>
    /// <returns>True when <paramref name="text"/> is a valid colour.</returns>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        color = new RgbColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>Converts a Win32 <c>COLORREF</c> (<c>0x00BBGGRR</c>, as stored by 5.4) to a colour.</summary>
    /// <param name="colorRef">The COLORREF value.</param>
    /// <returns>The colour.</returns>
    public static RgbColor FromColorRef(uint colorRef) =>
        new((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));

    /// <summary>Converts the colour to a Win32 <c>COLORREF</c> (<c>0x00BBGGRR</c>).</summary>
    /// <returns>The COLORREF value.</returns>
    public uint ToColorRef() => (uint)(B << 16 | G << 8 | R);

    /// <summary>Converts the colour to a packed BGRA pixel (see <see cref="Bgra32"/>).</summary>
    /// <param name="alpha">Alpha of the pixel (straight alpha).</param>
    /// <returns>The packed pixel.</returns>
    public uint ToBgra32(byte alpha = 255) => Bgra32.Pack(R, G, B, alpha);

    /// <summary>Formats the colour as <c>#RRGGBB</c> (upper-case hexadecimal).</summary>
    /// <returns>The formatted colour.</returns>
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Reads and writes <see cref="RgbColor"/> as a <c>"#RRGGBB"</c> JSON string.</summary>
public sealed class RgbColorJsonConverter : JsonConverter<RgbColor>
{
    /// <inheritdoc />
    public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        RgbColor.TryParse(reader.GetString(), out RgbColor color)
            ? color
            : throw new JsonException("Expected a \"#RRGGBB\" colour.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
