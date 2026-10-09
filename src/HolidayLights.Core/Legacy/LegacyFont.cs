using System.Buffers.Binary;
using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The 5.4 "Screen Saver Message Font" (a 60-byte LOGFONTA) mapped to the 6.0 font (PRODUCT-SPEC 6.8.2): family =
/// <c>lfFaceName</c>, size = round(|lfHeight| x 72 / 96) pt clamped to 18-100, bold = <c>lfWeight</c> &gt;= 600, italic,
/// underline and strikeout from their bytes.
/// </summary>
internal static class LegacyFont
{
    /// <summary>The size of a LOGFONTA.</summary>
    public const int Size = 60;

    /// <summary>
    /// Decodes a LOGFONTA. A shorter value overwrites the start of the 5.4 default (Arial, -48, 700) as 5.4's fixed-size
    /// read did; a missing or longer value is the default. An empty face becomes Arial and the size is clamped.
    /// </summary>
    /// <param name="data">The REG_BINARY value, or null when missing.</param>
    /// <returns>The font.</returns>
    public static SaverFont Decode(byte[]? data)
    {
        byte[] logFont = DefaultLogFont();
        if (data is not null && data.Length <= Size)
        {
            data.CopyTo(logFont, 0);
        }

        int height = BinaryPrimitives.ReadInt32LittleEndian(logFont);
        int weight = BinaryPrimitives.ReadInt32LittleEndian(logFont.AsSpan(16));
        int points = (int)Math.Round(Math.Abs((long)height) * 72 / 96.0, MidpointRounding.AwayFromZero);
        return ValueRules.CleanFont(new SaverFont
        {
            Family = LegacyText.DecodeTerminated(logFont.AsSpan(28, 32)),
            SizePt = points,
            Bold = weight >= 600,
            Italic = logFont[20] != 0,
            Underline = logFont[21] != 0,
            Strikeout = logFont[22] != 0,
        });
    }

    private static byte[] DefaultLogFont()
    {
        byte[] logFont = new byte[Size];
        BinaryPrimitives.WriteInt32LittleEndian(logFont, -48);
        BinaryPrimitives.WriteInt32LittleEndian(logFont.AsSpan(16), 700);
        "Arial"u8.CopyTo(logFont.AsSpan(28));
        return logFont;
    }
}
