using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>Pictures, GIFs and slot-table helpers for the Bulb Factory tests.</summary>
internal static class WritingTestData
{
    /// <summary>Opaque red, green, blue and white (straight BGRA).</summary>
    public const uint Red = 0xFFFF0000;

    /// <summary>Opaque green.</summary>
    public const uint Green = 0xFF00FF00;

    /// <summary>Opaque blue.</summary>
    public const uint Blue = 0xFF0000FF;

    /// <summary>Opaque white.</summary>
    public const uint White = 0xFFFFFFFF;

    /// <summary>The folder of the 1,501 bundled <c>.bul</c> files in the test output.</summary>
    public static string BundledBulbsFolder => Path.Combine(TestPaths.ContentFolder, "Bulbs");

    /// <summary>A picture filled with one colour.</summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Straight BGRA colour.</param>
    /// <returns>The picture.</returns>
    public static Rgba32Image Solid(int width, int height, uint color)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, color);
        return new Rgba32Image(width, height, pixels);
    }

    /// <summary>A one-frame GIF of one colour (distinct colours give distinct GIFs).</summary>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Straight BGRA colour.</param>
    /// <returns>The GIF bytes.</returns>
    public static byte[] SolidGif(int width, int height, uint color) => GifEncoder.Encode([Solid(width, height, color)]);

    /// <summary>A distinct 2-frame 32 x 32 GIF for each number (a light bulb that alternates two colours).</summary>
    /// <param name="number">Any number; equal numbers give equal GIFs.</param>
    /// <returns>The GIF bytes.</returns>
    public static byte[] NumberedGif(int number)
    {
        uint on = 0xFF000000 | ((uint)number * 2654435761u & 0xFFFFFF);
        return GifEncoder.Encode([Solid(32, 32, on), Solid(32, 32, on ^ 0x808080)]);
    }

    /// <summary>A bulb document whose 9 slots use one GIF, with a name.</summary>
    /// <param name="gif">The GIF.</param>
    /// <param name="name">The bulb name.</param>
    /// <returns>The document.</returns>
    public static BulbDocument Document(byte[] gif, string name = "Test Bulb") =>
        GifBulbFactory.Create(gif, name, "Pat Smith", 2026);

    /// <summary>Every (slot, flavor) a 5.4 file draws: preview, the 4 corners and each side's flavors before its first -1.</summary>
    /// <param name="file">A parsed file.</param>
    /// <returns>The references.</returns>
    public static IEnumerable<(CellSlot Slot, int Flavor)> References(BulFile file)
    {
        yield return (CellSlot.Preview, 0);
        foreach (Corner corner in CellSlots.Corners)
        {
            yield return (corner.ToSlot(), 0);
        }

        foreach (Side side in CellSlots.Sides)
        {
            for (int flavor = 0; flavor < file.GetFlavorCount(side); flavor++)
            {
                yield return (side.ToSlot(), flavor);
            }
        }
    }

    /// <summary>How many drawable slots use each entry.</summary>
    /// <param name="file">A parsed file.</param>
    /// <returns>Entry index to number of uses.</returns>
    public static Dictionary<int, int> CountUses(BulFile file)
    {
        var uses = new Dictionary<int, int>();
        foreach ((CellSlot slot, int flavor) in References(file))
        {
            int index = file.GetEntryIndex(slot, flavor);
            uses[index] = uses.GetValueOrDefault(index) + 1;
        }

        return uses;
    }

    /// <summary>The entry of <paramref name="other"/> shown by a slot that shows entry <paramref name="index"/> of <paramref name="file"/>.</summary>
    /// <param name="file">The file the entry belongs to.</param>
    /// <param name="index">An entry of <paramref name="file"/> that some slot uses.</param>
    /// <param name="other">A file with the same slots.</param>
    /// <returns>The entry index in <paramref name="other"/>.</returns>
    public static int EntryShowingSameSlot(BulFile file, int index, BulFile other)
    {
        (CellSlot slot, int flavor) = References(file).First(r => file.GetEntryIndex(r.Slot, r.Flavor) == index);
        return other.GetEntryIndex(slot, flavor);
    }

    /// <summary>CRC-32/BZIP2 computed bit by bit (an independent reference for the table-driven writer).</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The CRC.</returns>
    public static uint ReferenceCrc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80000000) != 0 ? crc << 1 ^ 0x04C11DB7 : crc << 1;
            }
        }

        return ~crc;
    }
}
