using System.Buffers.Binary;

namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>Result of encoding a bulb document.</summary>
/// <param name="Bytes">The complete <c>.bul</c> file.</param>
/// <param name="CharactersReplaced">True when characters outside Windows-1252 were replaced by "?" ("Some characters can't be saved in a bulb file and were replaced.").</param>
public sealed record BulFileContent(byte[] Bytes, bool CharactersReplaced);

/// <summary>
/// Writes standard <c>.bul</c> version 4 files exactly as Holiday Lights 5.4 writes them (PRODUCT-SPEC 6.3): 0x57C-byte
/// header, up to 37 GIF entries stored verbatim and de-duplicated by CRC-32/BZIP2, the <c>categ:</c> record,
/// <c>fileSize</c>, <c>locked</c> = 0, <c>unsent</c> = 0, copyright status 0, no region caches, no sender record,
/// Windows-1252 strings. Holiday Lights 5.4 can read the result. Owner: bulb-factory.
/// </summary>
/// <remarks>
/// The layout is the one 5.4's compaction produced: the header, the GIFs in entry order, then the
/// <c>categ:</c> record. Entries are numbered densely in the order of <see cref="BulbDocument.Gifs"/>; each records the
/// number of slots that use it (<c>refCount</c>) and its <c>constantMask</c> flag, worked out from the pixels.
/// </remarks>
public static class BulFileWriter
{
    /// <summary>The format version every 5.4 writer used.</summary>
    public const int Version = 4;

    /// <summary>The largest file written: <see cref="BulFile"/> treats bigger files as damaged.</summary>
    public const int MaxFileSize = 64 * 1024 * 1024;

    private const int HeaderSize = 0x57C;
    private const int OffsetVersion = 0x008;
    private const int OffsetBulbId = 0x00C;
    private const int OffsetName = 0x01C;
    private const int OffsetDescription = 0x06C;
    private const int OffsetCopyright = 0x0BC;
    private const int OffsetAuthor = 0x10C;
    private const int OffsetPreviewY = 0x15C;
    private const int OffsetPreviewX = 0x160;
    private const int OffsetSlotPreview = 0x164;
    private const int OffsetSlotCorners = 0x168;
    private const int OffsetSlotSides = 0x178;
    private const int OffsetEntries = 0x1F8;
    private const int EntryStride = 0x14;
    private const int OffsetCategories = 0x574;
    private const int OffsetFileSize = 0x578;
    private const string TemporaryExtension = ".tmp";

    private static ReadOnlySpan<byte> Magic => "bluBrgiT"u8;

    private static ReadOnlySpan<byte> CategoryPrefix => "categ:"u8;

    /// <summary>Encodes a document (compacted: unused GIFs dropped, flavors packed).</summary>
    /// <remarks>
    /// Texts longer than 79 characters are cut; the list-preview window is kept inside the picture; categories follow
    /// <see cref="BulCategories.Join"/> (names that would make the record too long for 5.4 are left out).
    /// </remarks>
    /// <param name="document">The document; it must have a name and every required slot (preview, 4 corners, flavor 1 of each side).</param>
    /// <param name="bulbId">The header bulb id: a fresh id not used by any loaded bulb.</param>
    /// <returns>The file content.</returns>
    /// <exception cref="ArgumentException">The document has no name or lacks a required animation.</exception>
    /// <exception cref="InvalidOperationException">The file would be larger than <see cref="MaxFileSize"/>.</exception>
    public static BulFileContent Encode(BulbDocument document, int bulbId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            throw new ArgumentException("The bulb has no name.", nameof(document));
        }

        CellSlot[] missing = [.. document.MissingSlots];
        if (missing.Length > 0)
        {
            throw new ArgumentException($"The bulb has no animation for the {missing[0]} slot.", nameof(document));
        }

        byte[] categories = BulText.Encode(BulCategories.Join(document.Categories, out _), out bool categoriesReplaced);
        long size = HeaderSize + document.Gifs.Sum(g => (long)g.Length) + CategoryPrefix.Length + categories.Length + 1;
        if (size > MaxFileSize)
        {
            throw new InvalidOperationException($"The bulb file would be {size} bytes long; bulb files can hold at most {MaxFileSize} bytes.");
        }

        var bytes = new byte[size];
        Span<byte> file = bytes;
        Magic.CopyTo(file);
        Write(file, OffsetVersion, Version);
        Write(file, OffsetBulbId, bulbId);
        bool replaced = WriteTexts(file, document);
        RectI window = document.PreviewWindow;
        Write(file, OffsetPreviewY, window.Top);
        Write(file, OffsetPreviewX, window.Left);
        int[] uses = WriteSlotTable(file, document);
        int offset = WriteEntries(file, document.Gifs, uses);
        Write(file, OffsetCategories, offset);
        CategoryPrefix.CopyTo(file[offset..]);
        categories.CopyTo(file[(offset + CategoryPrefix.Length)..]);
        Write(file, OffsetFileSize, bytes.Length);
        return new BulFileContent(bytes, replaced || categoriesReplaced);
    }

    /// <summary>Writes atomically: <c>&lt;path&gt;.tmp</c>, then replaces <paramref name="path"/>.</summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The encoded file.</param>
    /// <exception cref="IOException">The file cannot be written; the target is unchanged and no temporary file is left.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or file is not writable; the target is unchanged.</exception>
    public static void WriteAtomically(string path, BulFileContent content)
    {
        string temporary = WriteTemporaryFile(path, content);
        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// The first half of <see cref="WriteAtomically"/>: writes the content to <c>&lt;path&gt;.tmp</c> and flushes it to the
    /// disk. Bulb Editing moves the previous file to the holding folder before it moves this one into place.
    /// </summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The encoded file.</param>
    /// <returns>The temporary file (<c>&lt;path&gt;.tmp</c>).</returns>
    /// <exception cref="IOException">The file cannot be written; no temporary file is left.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public static string WriteTemporaryFile(string path, BulFileContent content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);
        string temporary = path + TemporaryExtension;
        try
        {
            using var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(content.Bytes);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        return temporary;
    }

    private static void Write(Span<byte> span, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(span[offset..], value);

    /// <summary>Writes the four header strings; returns true when a character had to be replaced.</summary>
    private static bool WriteTexts(Span<byte> file, BulbDocument document) =>
        WriteText(file, OffsetName, document.Name)
        | WriteText(file, OffsetDescription, document.Description)
        | WriteText(file, OffsetCopyright, document.Copyright)
        | WriteText(file, OffsetAuthor, document.Author);

    /// <summary>Writes a NUL-terminated header string; the rest of its 80-byte field stays zero.</summary>
    private static bool WriteText(Span<byte> file, int offset, string text)
    {
        BulText.EncodeField(text, out bool replaced).CopyTo(file[offset..]);
        return replaced;
    }

    /// <summary>Writes the slot table (sides packed, -1 after the last flavor); returns how many slots use each entry.</summary>
    private static int[] WriteSlotTable(Span<byte> file, BulbDocument document)
    {
        var entries = new Dictionary<BulbGif, int>(ReferenceEqualityComparer.Instance);
        foreach (BulbGif gif in document.Gifs)
        {
            entries.Add(gif, entries.Count);
        }

        var uses = new int[entries.Count];
        int Use(BulbGif gif)
        {
            int index = entries[gif];
            uses[index]++;
            return index;
        }

        Write(file, OffsetSlotPreview, Use(document.GetGif(CellSlot.Preview, 0)!));
        foreach (Corner corner in CellSlots.Corners)
        {
            Write(file, OffsetSlotCorners + 4 * (int)corner, Use(document.GetGif(corner.ToSlot(), 0)!));
        }

        foreach (Side side in CellSlots.Sides)
        {
            IReadOnlyList<BulbGif> flavors = document.GetFlavors(side);
            for (int flavor = 0; flavor < BulbDocument.MaxFlavors; flavor++)
            {
                Write(file, OffsetSlotSides + 0x20 * (int)side + 4 * flavor, flavor < flavors.Count ? Use(flavors[flavor]) : -1);
            }
        }

        return uses;
    }

    /// <summary>Writes the entry table and the GIFs after the header, in entry order; returns the offset after the last GIF.</summary>
    private static int WriteEntries(Span<byte> file, IReadOnlyList<BulbGif> gifs, int[] uses)
    {
        int offset = HeaderSize;
        for (int index = 0; index < gifs.Count; index++)
        {
            BulbGif gif = gifs[index];
            Span<byte> entry = file.Slice(OffsetEntries + EntryStride * index, EntryStride);
            Write(entry, 0x00, gif.Length);
            Write(entry, 0x04, offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[0x08..], gif.Crc);
            Write(entry, 0x0C, uses[index]);
            Write(entry, 0x10, gif.HasConstantMask ? 1 : 0);
            gif.Bytes.Span.CopyTo(file[offset..]);
            offset += gif.Length;
        }

        return offset;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Left for the next save, which overwrites it.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
