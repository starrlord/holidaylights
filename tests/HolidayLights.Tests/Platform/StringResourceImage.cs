using System.Buffers.Binary;
using System.Text;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// Writes a minimal 32-bit Windows image whose only content is a string table resource, so tests can stand in for a
/// program that is recognized by its string resources (the Holiday Lights 5.4 screen saver: string 1 is
/// "Holiday Lights", string 2 is "TigerTechHolidayLights") without shipping that program.
/// </summary>
internal static class StringResourceImage
{
    private const int FileAlignment = 0x200;
    private const int SectionAlignment = 0x1000;
    private const int ResourceRva = SectionAlignment;
    private const int OptionalHeaderSize = 0xE0;

    /// <summary>The string resources of the Holiday Lights 5.4 screen saver that identify it.</summary>
    public static IReadOnlyDictionary<int, string> HolidayLights54Saver { get; } = new Dictionary<int, string>
    {
        [1] = "Holiday Lights",
        [2] = "TigerTechHolidayLights",
    };

    /// <summary>Writes the image.</summary>
    /// <param name="path">The file to create.</param>
    /// <param name="strings">String ids (0-65535) and their text.</param>
    public static void Write(string path, IReadOnlyDictionary<int, string> strings) => File.WriteAllBytes(path, Build(strings));

    /// <summary>Builds the image: headers, then one <c>.rsrc</c> section with an RT_STRING directory (language neutral).</summary>
    /// <param name="strings">String ids (0-65535) and their text.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] Build(IReadOnlyDictionary<int, string> strings)
    {
        byte[] resources = BuildResourceSection(strings);
        int rawSize = Align(resources.Length, FileAlignment);
        var image = new byte[FileAlignment + rawSize];
        Span<byte> span = image;

        // DOS header: "MZ" and the offset of the NT headers.
        span[0] = (byte)'M';
        span[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], 0x40);

        // NT signature and file header (i386, one section, executable image, 32-bit).
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(span[0x40..]);
        Span<byte> file = span[0x44..];
        BinaryPrimitives.WriteUInt16LittleEndian(file, 0x014C);
        BinaryPrimitives.WriteUInt16LittleEndian(file[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file[16..], OptionalHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file[18..], 0x0102);

        // Optional header (PE32).
        Span<byte> optional = span[0x58..];
        BinaryPrimitives.WriteUInt16LittleEndian(optional, 0x10B);
        BinaryPrimitives.WriteInt32LittleEndian(optional[8..], rawSize);
        BinaryPrimitives.WriteInt32LittleEndian(optional[20..], ResourceRva);
        BinaryPrimitives.WriteInt32LittleEndian(optional[24..], ResourceRva);
        BinaryPrimitives.WriteInt32LittleEndian(optional[28..], 0x00400000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[32..], SectionAlignment);
        BinaryPrimitives.WriteInt32LittleEndian(optional[36..], FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[40..], 5);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[42..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[48..], 5);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[50..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(optional[56..], ResourceRva + Align(resources.Length, SectionAlignment));
        BinaryPrimitives.WriteInt32LittleEndian(optional[60..], FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[68..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(optional[72..], 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[76..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[80..], 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[84..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[92..], 16);
        BinaryPrimitives.WriteInt32LittleEndian(optional[(96 + 2 * 8)..], ResourceRva);
        BinaryPrimitives.WriteInt32LittleEndian(optional[(96 + 2 * 8 + 4)..], resources.Length);

        // Section header: .rsrc, initialized read-only data.
        Span<byte> section = span[(0x58 + OptionalHeaderSize)..];
        Encoding.ASCII.GetBytes(".rsrc").CopyTo(section);
        BinaryPrimitives.WriteInt32LittleEndian(section[8..], resources.Length);
        BinaryPrimitives.WriteInt32LittleEndian(section[12..], ResourceRva);
        BinaryPrimitives.WriteInt32LittleEndian(section[16..], rawSize);
        BinaryPrimitives.WriteInt32LittleEndian(section[20..], FileAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(section[36..], 0x40000040);

        resources.CopyTo(span[FileAlignment..]);
        return image;
    }

    /// <summary>
    /// The resource section: type directory (RT_STRING) -> one name directory entry per block of 16 strings -> one
    /// language entry (neutral) each -> data entries -> the blocks (16 counted UTF-16 strings each).
    /// </summary>
    private static byte[] BuildResourceSection(IReadOnlyDictionary<int, string> strings)
    {
        const int DirectorySize = 16;
        const int EntrySize = 8;
        const int DataEntrySize = 16;
        const uint SubdirectoryFlag = 0x80000000;

        int[] blocks = [.. strings.Keys.Select(id => (id >> 4) + 1).Distinct().Order()];
        int typeDirectory = 0;
        int nameDirectory = typeDirectory + DirectorySize + EntrySize;
        int languageDirectories = nameDirectory + DirectorySize + blocks.Length * EntrySize;
        int dataEntries = languageDirectories + blocks.Length * (DirectorySize + EntrySize);
        int blockData = dataEntries + blocks.Length * DataEntrySize;

        var blockBytes = new List<byte[]>();
        foreach (int block in blocks)
        {
            var bytes = new List<byte>();
            for (int index = 0; index < 16; index++)
            {
                string text = strings.TryGetValue(((block - 1) << 4) + index, out string? value) ? value : string.Empty;
                bytes.AddRange(BitConverter.GetBytes((ushort)text.Length));
                bytes.AddRange(Encoding.Unicode.GetBytes(text));
            }

            blockBytes.Add([.. bytes]);
        }

        var section = new byte[blockData + blockBytes.Sum(b => Align(b.Length, 4))];
        Span<byte> span = section;

        WriteDirectory(span[typeDirectory..], 1);
        WriteEntry(span[(typeDirectory + DirectorySize)..], 6, SubdirectoryFlag | (uint)nameDirectory);

        WriteDirectory(span[nameDirectory..], blocks.Length);
        int dataOffset = blockData;
        for (int i = 0; i < blocks.Length; i++)
        {
            int languageDirectory = languageDirectories + i * (DirectorySize + EntrySize);
            int dataEntry = dataEntries + i * DataEntrySize;
            WriteEntry(span[(nameDirectory + DirectorySize + i * EntrySize)..], (uint)blocks[i], SubdirectoryFlag | (uint)languageDirectory);
            WriteDirectory(span[languageDirectory..], 1);
            WriteEntry(span[(languageDirectory + DirectorySize)..], 0, (uint)dataEntry);
            BinaryPrimitives.WriteInt32LittleEndian(span[dataEntry..], ResourceRva + dataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(span[(dataEntry + 4)..], blockBytes[i].Length);
            blockBytes[i].CopyTo(span[dataOffset..]);
            dataOffset += Align(blockBytes[i].Length, 4);
        }

        return section;
    }

    private static void WriteDirectory(Span<byte> target, int idEntries) =>
        BinaryPrimitives.WriteUInt16LittleEndian(target[14..], (ushort)idEntries);

    private static void WriteEntry(Span<byte> target, uint id, uint offset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target, id);
        BinaryPrimitives.WriteUInt32LittleEndian(target[4..], offset);
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
