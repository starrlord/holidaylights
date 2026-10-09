using System.Buffers.Binary;
using HolidayLights.Core.Bulbs;

namespace HolidayLights.Tests.Bulbs;

/// <summary>Builds <c>.bul</c> files byte by byte for tests.</summary>
internal sealed class TestBul
{
    private const int HeaderSize = 0x57C;

    /// <summary>Header bulb id.</summary>
    public uint BulbId { get; init; } = 123456;

    /// <summary>Header <c>locked</c>.</summary>
    public bool Locked { get; init; } = true;

    /// <summary>Name.</summary>
    public string Name { get; init; } = "Test Bulb";

    /// <summary>Description.</summary>
    public string Description { get; init; } = "A bulb for tests.";

    /// <summary>Copyright.</summary>
    public string Copyright { get; init; } = "Copyright 2026 Tester";

    /// <summary>Author.</summary>
    public string Author { get; init; } = "Tester\r\ntester@example.com";

    /// <summary>Preview window left.</summary>
    public int PreviewX { get; init; }

    /// <summary>Preview window top.</summary>
    public int PreviewY { get; init; }

    /// <summary>The GIF of each entry index.</summary>
    public required IReadOnlyList<byte[]> Gifs { get; init; }

    /// <summary>Entry of the preview.</summary>
    public int PreviewEntry { get; init; }

    /// <summary>Entries of the corners TL, TR, BR, BL.</summary>
    public int[] Corners { get; init; } = [0, 0, 0, 0];

    /// <summary>Flavor entries per side (Top, Right, Bottom, Left).</summary>
    public int[][] Sides { get; init; } = [[0], [0], [0], [0]];

    /// <summary>The <c>categ:</c> text, or null for no record.</summary>
    public string? Categories { get; init; } = "Christmas|Winter";

    /// <summary>Record the file size in the header.</summary>
    public bool RecordSize { get; init; } = true;

    /// <summary>Bytes appended after everything (e.g. a region cache), not counted by <see cref="RecordSize"/>.</summary>
    public byte[] Trailing { get; init; } = [];

    /// <summary>Encodes the file.</summary>
    /// <returns>The bytes.</returns>
    public byte[] Build()
    {
        var file = new List<byte>(new byte[HeaderSize]);
        var offsets = new int[Gifs.Count];
        for (int i = 0; i < Gifs.Count; i++)
        {
            offsets[i] = file.Count;
            file.AddRange(Gifs[i]);
        }

        int categoriesOffset = 0;
        if (Categories is not null)
        {
            categoriesOffset = file.Count;
            file.AddRange("categ:"u8.ToArray());
            file.AddRange(Ansi(Categories));
            file.Add(0);
        }

        byte[] bytes = [.. file, .. Trailing];
        Span<byte> h = bytes;
        "bluBrgiT"u8.CopyTo(h);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x08..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(h[0x0C..], BulbId);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x14..], Locked ? 1 : 0);
        WriteString(h[0x1C..], Name);
        WriteString(h[0x6C..], Description);
        WriteString(h[0xBC..], Copyright);
        WriteString(h[0x10C..], Author);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x15C..], PreviewY);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x160..], PreviewX);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x164..], PreviewEntry);
        for (int c = 0; c < 4; c++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(h[(0x168 + 4 * c)..], Corners[c]);
        }

        for (int s = 0; s < 4; s++)
        {
            for (int f = 0; f < 8; f++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(h[(0x178 + 0x20 * s + 4 * f)..], f < Sides[s].Length ? Sides[s][f] : -1);
            }
        }

        for (int i = 0; i < Gifs.Count; i++)
        {
            Span<byte> entry = h[(0x1F8 + 0x14 * i)..];
            BinaryPrimitives.WriteInt32LittleEndian(entry, Gifs[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], offsets[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], Crc32Bzip2(Gifs[i]));
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], 9);
            BinaryPrimitives.WriteInt32LittleEndian(entry[16..], 1);
        }

        BinaryPrimitives.WriteInt32LittleEndian(h[0x574..], categoriesOffset);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x578..], RecordSize ? file.Count : 0);
        return bytes;
    }

    /// <summary>Writes the file.</summary>
    /// <param name="path">The target.</param>
    /// <returns><paramref name="path"/>.</returns>
    public string WriteTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Build());
        return path;
    }

    /// <summary>Windows-1252 bytes of a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Ansi(string text) => [.. text.Select(Windows1252.ToByte)];

    /// <summary>CRC-32/BZIP2 (MSB first, poly 0x04C11DB7, init and xorout 0xFFFFFFFF).</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The CRC.</returns>
    public static uint Crc32Bzip2(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 0x80000000) != 0 ? crc << 1 ^ 0x04C11DB7 : crc << 1;
            }
        }

        return ~crc;
    }

    private static void WriteString(Span<byte> field, string text)
    {
        byte[] bytes = Ansi(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, 79)).CopyTo(field);
    }
}
