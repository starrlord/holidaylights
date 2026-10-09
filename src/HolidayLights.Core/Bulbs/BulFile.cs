using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HolidayLights.Core.Bulbs;

/// <summary>One of the 37 animation entries of a <c>.bul</c> file.</summary>
/// <param name="Index">Entry index 0-36.</param>
/// <param name="Offset">Absolute file offset of the GIF.</param>
/// <param name="Size">GIF byte count (0 = unused entry).</param>
/// <param name="Crc">Stored CRC-32/BZIP2 of the GIF bytes.</param>
/// <param name="RefCount">Stored slot reference count (not used at run time).</param>
/// <param name="StoredConstantMask">Stored constantMask flag (informational; recompute when needed).</param>
/// <param name="Gif">The verbatim GIF bytes (empty for an unused entry).</param>
public sealed record BulGifEntry(int Index, int Offset, int Size, uint Crc, int RefCount, bool StoredConstantMask, ReadOnlyMemory<byte> Gif);

/// <summary>
/// A parsed <c>.bul</c> container (0x57C-byte header, slot table, 37 GIF entries, <c>categ:</c> record), read leniently:
/// a damaged file is reported in <see cref="Problems"/>, not thrown. Owner: core-bulbs.
/// </summary>
/// <remarks>
/// Used by the catalog (add-on bulbs), Bulb Editing (bulb-factory, to load a file for editing), the 5.4 import
/// (core-settings: header id and content identity) and the screen saver (an add-on bulb as the animation). Strings are
/// decoded from Windows-1252; bytes after the terminating NUL are ignored.
/// </remarks>
public sealed class BulFile
{
    /// <summary>Number of animation entries in the header.</summary>
    public const int EntryCount = 37;

    /// <summary>Most flavors per side.</summary>
    public const int MaxFlavors = 8;

    /// <summary>Size of the fixed header.</summary>
    private const int HeaderSize = 0x57C;

    /// <summary>Largest file read; anything bigger cannot be a bulb and is reported as damaged without being loaded.</summary>
    private const long MaxFileSize = 64L * 1024 * 1024;

    private const int OffsetBulbId = 0x00C;
    private const int OffsetCopyrightStatus = 0x010;
    private const int OffsetLocked = 0x014;
    private const int OffsetUnsent = 0x018;
    private const int OffsetName = 0x01C;
    private const int OffsetDescription = 0x06C;
    private const int OffsetCopyright = 0x0BC;
    private const int OffsetAuthor = 0x10C;
    private const int StringFieldLength = 80;
    private const int OffsetPreviewY = 0x15C;
    private const int OffsetPreviewX = 0x160;
    private const int OffsetSlotPreview = 0x164;
    private const int OffsetSlotCorners = 0x168;
    private const int OffsetSlotSides = 0x178;
    private const int OffsetEntries = 0x1F8;
    private const int EntryStride = 0x14;
    private const int OffsetCategories = 0x574;
    private const int OffsetFileSize = 0x578;

    private const int MaxCategoriesLength = 255;
    private const int MaxCategoryNameLength = 39;

    private static readonly string[] CornerNames = ["top-left corner", "top-right corner", "bottom-right corner", "bottom-left corner"];
    private static readonly string[] SideNames = ["top side", "right side", "bottom side", "left side"];

    private BulFile()
    {
    }

    /// <summary>The file path, or null when parsed from memory.</summary>
    public string? FilePath { get; private init; }

    /// <summary>Header 0x0C: the stored bulb id (5.4 run-time ids bump it while it collides).</summary>
    public int LegacyId { get; private init; }

    /// <summary>Header 0x10: copyright status 0-3 (informational).</summary>
    public int CopyrightStatus { get; private init; }

    /// <summary>Header 0x14: <c>locked</c> (non-zero = artwork not editable; every bundled bulb).</summary>
    public bool Locked { get; private init; }

    /// <summary>Header 0x18: <c>unsent</c>.</summary>
    public bool Unsent { get; private init; }

    /// <summary>Header 0x1C: name.</summary>
    public string Name { get; private init; } = "";

    /// <summary>Header 0x6C: description.</summary>
    public string Description { get; private init; } = "";

    /// <summary>Header 0xBC: copyright.</summary>
    public string Copyright { get; private init; } = "";

    /// <summary>Header 0x10C: author ("Name\r\nE-mail").</summary>
    public string Author { get; private init; } = "";

    /// <summary>Header 0x160: left of the 32 x 32 list-preview window in frame 0 of the preview animation.</summary>
    public int PreviewX { get; private init; }

    /// <summary>Header 0x15C: top of the list-preview window.</summary>
    public int PreviewY { get; private init; }

    /// <summary>Header 0x164: entry index of the bulb-list preview.</summary>
    public int PreviewEntry { get; private init; }

    /// <summary>Header 0x168: entry index per corner, order TL, TR, BR, BL.</summary>
    public IReadOnlyList<int> CornerEntries { get; private init; } = [];

    /// <summary>Header 0x178: per side (Top, Right, Bottom, Left) up to 8 flavor entry indices; -1 = unused.</summary>
    public IReadOnlyList<IReadOnlyList<int>> SideEntries { get; private init; } = [];

    /// <summary>The 37 entries (unused ones included, with <see cref="BulGifEntry.Size"/> 0).</summary>
    public IReadOnlyList<BulGifEntry> Entries { get; private init; } = [];

    /// <summary>The categories of the <c>categ:</c> record, split on '|', trimmed, empties removed.</summary>
    public IReadOnlyList<string> Categories { get; private init; } = [];

    /// <summary>Header 0x578: the recorded file size (0 = not recorded).</summary>
    public int RecordedFileSize { get; private init; }

    /// <summary>Reasons the 5.4 loader would reject the file; empty for a good file.</summary>
    public IReadOnlyList<string> Problems { get; private init; } = [];

    /// <summary>True when <see cref="Problems"/> is not empty.</summary>
    public bool IsDamaged => Problems.Count > 0;

    /// <summary>Reads and parses a file.</summary>
    /// <param name="path">The <c>.bul</c> file.</param>
    /// <returns>The parsed file (check <see cref="IsDamaged"/>).</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static BulFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaxFileSize)
            {
                return Rejected(path, "The file is too large to be a bulb file.");
            }

            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            return Parse(data, path);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new IOException(e.Message, e);
        }
    }

    /// <summary>Parses a file held in memory.</summary>
    /// <param name="data">The file bytes (kept by reference for <see cref="BulGifEntry.Gif"/>).</param>
    /// <param name="filePath">The path to report, if any.</param>
    /// <returns>The parsed file (check <see cref="IsDamaged"/>).</returns>
    public static BulFile Parse(ReadOnlyMemory<byte> data, string? filePath = null)
    {
        var problems = new List<string>();
        ReadOnlySpan<byte> span = data.Span;
        ReadOnlySpan<byte> header = span;
        if (span.Length < HeaderSize)
        {
            problems.Add("The file is shorter than a bulb file header.");
            var padded = new byte[HeaderSize];
            span.CopyTo(padded);
            header = padded;
        }

        if (!header[..8].SequenceEqual("bluBrgiT"u8))
        {
            problems.Add("The file is not a Holiday Lights bulb file.");
        }

        int previewEntry = ReadInt32(header, OffsetSlotPreview);
        int[] corners = ReadInt32s(header, OffsetSlotCorners, 4);
        var sides = new int[4][];
        for (int s = 0; s < sides.Length; s++)
        {
            sides[s] = ReadInt32s(header, OffsetSlotSides + s * 4 * MaxFlavors, MaxFlavors);
        }

        BulGifEntry[] entries = ReadEntries(header, data);
        uint recordedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetFileSize..]);

        CheckSlots(previewEntry, corners, sides, problems);
        CheckSize(span, recordedSize, entries, problems);
        IReadOnlyList<string> categories = ReadCategories(span, BinaryPrimitives.ReadUInt32LittleEndian(header[OffsetCategories..]), problems);

        return new BulFile
        {
            FilePath = filePath,
            LegacyId = ReadInt32(header, OffsetBulbId),
            CopyrightStatus = ReadInt32(header, OffsetCopyrightStatus),
            Locked = ReadInt32(header, OffsetLocked) != 0,
            Unsent = ReadInt32(header, OffsetUnsent) != 0,
            Name = ReadString(header, OffsetName),
            Description = ReadString(header, OffsetDescription),
            Copyright = ReadString(header, OffsetCopyright),
            Author = ReadString(header, OffsetAuthor),
            PreviewX = ReadInt32(header, OffsetPreviewX),
            PreviewY = ReadInt32(header, OffsetPreviewY),
            PreviewEntry = previewEntry,
            CornerEntries = corners,
            SideEntries = sides,
            Entries = entries,
            Categories = categories,
            RecordedFileSize = unchecked((int)recordedSize),
            Problems = problems,
        };
    }

    /// <summary>
    /// The bulb content identity (PRODUCT-SPEC 6.3): name, description, author, copyright, preview window, slot table and
    /// the size and CRC of every referenced GIF entry. Region caches, sender, <c>categ:</c>, flags and the bulb id are ignored.
    /// Two files are the same bulb when their keys are equal (ordinal).
    /// </summary>
    /// <remarks>
    /// The slot table is compared by what each drawable slot shows (the size and stored CRC of its GIF), so two files that
    /// order the same GIFs differently in their entry tables are the same bulb; flavors after a side's first -1 are not
    /// drawn and do not count.
    /// </remarks>
    /// <returns>A stable key (for example a SHA-256 over a canonical encoding).</returns>
    public string ComputeContentIdentity()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "holidaylights.bul-identity/1");
        AppendText(hash, Name);
        AppendText(hash, Description);
        AppendText(hash, Author);
        AppendText(hash, Copyright);
        AppendInt(hash, PreviewX);
        AppendInt(hash, PreviewY);
        AppendEntry(hash, PreviewEntry);
        foreach (int corner in CornerEntries)
        {
            AppendEntry(hash, corner);
        }

        foreach (Side side in CellSlots.Sides)
        {
            int count = GetFlavorCount(side);
            AppendInt(hash, count);
            for (int flavor = 0; flavor < count; flavor++)
            {
                AppendEntry(hash, SideEntries[(int)side][flavor]);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The 5.4 flavor count of a side: entries before the first -1.</summary>
    /// <param name="side">The side.</param>
    /// <returns>0-8 (at least 1 for an undamaged file).</returns>
    public int GetFlavorCount(Side side)
    {
        IReadOnlyList<int> flavors = SideEntries[(int)side];
        int count = 0;
        while (count < flavors.Count && flavors[count] != -1)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The entry a slot draws: the preview entry, a corner's entry, or for a side the entry of
    /// <c>flavor</c> modulo the side's flavor count.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Raw flavor index (ignored for corners and the preview).</param>
    /// <returns>The entry index as stored (it may be out of range in a damaged file), or -1 for a side without flavors.</returns>
    public int GetEntryIndex(CellSlot slot, int flavor)
    {
        if (slot == CellSlot.Preview)
        {
            return PreviewEntry;
        }

        if (slot.IsCorner())
        {
            return CornerEntries[(int)slot.ToCorner()];
        }

        Side side = slot.ToSide();
        int count = GetFlavorCount(side);
        return count == 0 ? -1 : SideEntries[(int)side][((flavor % count) + count) % count];
    }

    /// <summary>A damaged file that was not read: empty slots and entries and one problem.</summary>
    private static BulFile Rejected(string path, string reason)
    {
        BulFile empty = Parse(ReadOnlyMemory<byte>.Empty, path);
        return new BulFile
        {
            FilePath = path,
            CornerEntries = empty.CornerEntries,
            SideEntries = empty.SideEntries,
            Entries = empty.Entries,
            Problems = [reason],
        };
    }

    private static void CheckSlots(int previewEntry, int[] corners, int[][] sides, List<string> problems)
    {
        if (previewEntry == -1)
        {
            problems.Add("The bulb list preview has no animation.");
        }

        for (int c = 0; c < corners.Length; c++)
        {
            if (corners[c] == -1)
            {
                problems.Add($"The {CornerNames[c]} has no animation.");
            }
        }

        for (int s = 0; s < sides.Length; s++)
        {
            if (sides[s][0] == -1)
            {
                problems.Add($"The {SideNames[s]} has no animation.");
            }
        }
    }

    /// <summary>The 5.4 completeness check: the recorded size, or (when none) the last GIF must end with its trailer.</summary>
    private static void CheckSize(ReadOnlySpan<byte> data, uint recordedSize, BulGifEntry[] entries, List<string> problems)
    {
        if (recordedSize != 0)
        {
            if (recordedSize != data.Length)
            {
                problems.Add($"The file should be {recordedSize} bytes long but is {data.Length} bytes long.");
            }

            return;
        }

        BulGifEntry? last = null;
        foreach (BulGifEntry entry in entries)
        {
            if ((uint)entry.Offset > (uint)(last?.Offset ?? 0))
            {
                last = entry;
            }
        }

        if (last is null)
        {
            problems.Add("The file contains no animations.");
            return;
        }

        long end = (long)(uint)last.Offset + (uint)last.Size - 1;
        if (end < 0 || end >= data.Length || data[(int)end] != 0x3B)
        {
            problems.Add("The last animation in the file is incomplete.");
        }
    }

    private static IReadOnlyList<string> ReadCategories(ReadOnlySpan<byte> data, uint offset, List<string> problems)
    {
        if (offset == 0)
        {
            return [];
        }

        if (offset >= (uint)data.Length)
        {
            problems.Add("The category list is missing from the file.");
            return [];
        }

        ReadOnlySpan<byte> record = data[(int)offset..];
        if (!record.StartsWith("categ:"u8))
        {
            return [];
        }

        ReadOnlySpan<byte> text = record[6..];
        int end = text.IndexOf((byte)0);
        if (end >= 0)
        {
            text = text[..end];
        }

        if (text.Length > MaxCategoriesLength)
        {
            problems.Add($"The category list is longer than {MaxCategoriesLength} characters.");
            return [];
        }

        var names = new List<string>();
        foreach (string token in Windows1252.Decode(text).Split('|'))
        {
            string name = token.Trim(' ');
            if (name.Length > MaxCategoryNameLength)
            {
                problems.Add($"The category \"{name}\" is longer than {MaxCategoryNameLength} characters.");
            }
            else if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static BulGifEntry[] ReadEntries(ReadOnlySpan<byte> header, ReadOnlyMemory<byte> data)
    {
        var entries = new BulGifEntry[EntryCount];
        for (int i = 0; i < EntryCount; i++)
        {
            ReadOnlySpan<byte> e = header.Slice(OffsetEntries + i * EntryStride, EntryStride);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(e);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
            bool present = size != 0 && (ulong)offset + size <= (ulong)data.Length;
            entries[i] = new BulGifEntry(
                i,
                unchecked((int)offset),
                unchecked((int)size),
                BinaryPrimitives.ReadUInt32LittleEndian(e[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(e[12..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[16..]) != 0,
                present ? data.Slice((int)offset, (int)size) : ReadOnlyMemory<byte>.Empty);
        }

        return entries;
    }

    /// <summary>A C string in an 80-byte field; without a NUL inside the field it runs on into the next fields (5.4).</summary>
    private static string ReadString(ReadOnlySpan<byte> header, int offset)
    {
        ReadOnlySpan<byte> field = header.Slice(offset, StringFieldLength);
        return field.Contains((byte)0)
            ? Windows1252.DecodeCString(field)
            : Windows1252.DecodeCString(header[offset..HeaderSize]);
    }

    private static int ReadInt32(ReadOnlySpan<byte> header, int offset) => BinaryPrimitives.ReadInt32LittleEndian(header[offset..]);

    private static int[] ReadInt32s(ReadOnlySpan<byte> header, int offset, int count)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadInt32(header, offset + i * 4);
        }

        return values;
    }

    private void AppendEntry(IncrementalHash hash, int index)
    {
        if (index is >= 0 and < EntryCount && Entries[index].Size != 0)
        {
            AppendInt(hash, 1);
            AppendInt(hash, Entries[index].Size);
            AppendInt(hash, unchecked((int)Entries[index].Crc));
        }
        else
        {
            AppendInt(hash, 0);
            AppendInt(hash, index);
        }
    }

    private static void AppendText(IncrementalHash hash, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        AppendInt(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
