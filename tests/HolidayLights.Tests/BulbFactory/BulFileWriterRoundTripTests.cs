using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text.Json;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>
/// Every one of the 1,501 bundled bulbs is read with the core-bulbs reader, opened as a document, written by
/// <see cref="BulFileWriter"/> and read again: the content must be identical and the file must follow every 5.4 rule.
/// </summary>
public sealed class BulFileWriterRoundTripTests
{
    private const int HeaderSize = 0x57C;

    private static readonly Lazy<IReadOnlyList<JsonElement>> Goldens =
        new(() => [.. GoldenData.ReadJson("bul-frames.json.gz").RootElement.GetProperty("files").EnumerateArray()]);

    [Fact]
    public void EveryBundledBulb_KeepsItsContentThroughDocumentAndWriter()
    {
        var failures = new ConcurrentBag<string>();
        int checkedFiles = 0;
        Parallel.ForEach(Goldens.Value, golden =>
        {
            string name = golden.GetProperty("file").GetString()!;
            try
            {
                CheckRoundTrip(Path.Combine(WritingTestData.BundledBulbsFolder, name), golden);
                Interlocked.Increment(ref checkedFiles);
            }
            catch (InvalidDataException e)
            {
                failures.Add($"{name}: {e.Message}");
            }
        });

        Assert.Empty(failures.Order());
        Assert.Equal(1501, checkedFiles);
    }

    [Theory]
    [InlineData("10thBirthday.bul")]
    [InlineData("Gemstones.bul")]
    [InlineData("HalloweenBulbs.bul")]
    [InlineData("Arrow.bul")]
    [InlineData("ColoredKeysLarge.bul")]
    public void WritingTheWrittenFileAgain_GivesTheSameBytes(string name)
    {
        BulFile original = BulFile.Read(Path.Combine(WritingTestData.BundledBulbsFolder, name));
        byte[] once = BulFileWriter.Encode(BulbDocument.FromFile(original), original.LegacyId).Bytes;
        byte[] twice = BulFileWriter.Encode(BulbDocument.FromFile(BulFile.Parse(once)), original.LegacyId).Bytes;

        Assert.Equal(once, twice);
    }

    private static void CheckRoundTrip(string path, JsonElement golden)
    {
        BulFile original = BulFile.Read(path);
        BulFileContent content = BulFileWriter.Encode(BulbDocument.FromFile(original), original.LegacyId);
        BulFile copy = BulFile.Parse(content.Bytes);

        Require(!copy.IsDamaged, $"the written file is damaged: {string.Join("; ", copy.Problems)}");
        Require(!content.CharactersReplaced, "characters were replaced");
        Require(copy.ComputeContentIdentity() == original.ComputeContentIdentity(), "the content identity changed");
        Require(copy.Categories.SequenceEqual(original.Categories), "the categories changed");
        Require(copy.LegacyId == original.LegacyId, "the bulb id changed");
        Require(!copy.Locked && !copy.Unsent && copy.CopyrightStatus == 0, "the flags are not locked 0, unsent 0, status 0");
        Require(copy.RecordedFileSize == content.Bytes.Length, "the recorded file size is wrong");
        foreach ((CellSlot slot, int flavor) in WritingTestData.References(original))
        {
            ReadOnlyMemory<byte> before = original.Entries[original.GetEntryIndex(slot, flavor)].Gif;
            ReadOnlyMemory<byte> after = copy.Entries[copy.GetEntryIndex(slot, flavor)].Gif;
            Require(before.Span.SequenceEqual(after.Span), $"{slot} flavor {flavor + 1} shows another GIF");
        }

        CheckEntries(content.Bytes, copy, original, golden);
        CheckLayout(content.Bytes, original);
    }

    /// <summary>Every stored entry is used and numbered densely; its CRC, use count and constantMask flag are right.</summary>
    private static void CheckEntries(byte[] bytes, BulFile copy, BulFile original, JsonElement golden)
    {
        Dictionary<int, int> uses = WritingTestData.CountUses(copy);
        Dictionary<int, bool> goldenMask = golden.GetProperty("entries").EnumerateArray()
            .ToDictionary(e => e.GetProperty("index").GetInt32(), e => e.GetProperty("constantMask").GetInt32() == 1);
        int used = copy.Entries.Count(e => e.Size != 0);
        Require(used == uses.Count, "an entry is stored but not used, or a slot uses an empty entry");
        Require(copy.Entries.Take(used).All(e => e.Size != 0), "the used entries are not numbered densely");
        foreach (BulGifEntry entry in copy.Entries.Take(used))
        {
            Require(entry.Crc == WritingTestData.ReferenceCrc(entry.Gif.Span), $"entry {entry.Index} has a wrong CRC");
            Require(entry.RefCount == uses[entry.Index], $"entry {entry.Index} records {entry.RefCount} uses instead of {uses[entry.Index]}");
            int originalIndex = WritingTestData.EntryShowingSameSlot(copy, entry.Index, original);
            Require(entry.StoredConstantMask == goldenMask[originalIndex], $"entry {entry.Index} has a wrong constantMask");
        }

        Require(bytes.AsSpan(0x4DC, 37 * 4).IndexOfAnyExcept((byte)0) < 0, "region cache offsets are not 0");
        Require(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x570)) == 0, "a sender record is referenced");
    }

    /// <summary>After the header come the GIFs, verbatim and in the original entry order, then the <c>categ:</c> record.</summary>
    private static void CheckLayout(byte[] bytes, BulFile original)
    {
        HashSet<int> used = [.. WritingTestData.CountUses(original).Keys];
        byte[] gifs = [.. original.Entries.Where(e => used.Contains(e.Index)).SelectMany(e => e.Gif.ToArray())];
        Require(bytes.AsSpan(HeaderSize, gifs.Length).SequenceEqual(gifs), "the GIFs are not stored verbatim in entry order after the header");
        int categories = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x574));
        Require(categories == HeaderSize + gifs.Length, "the categ: record does not follow the GIFs");
        Require(bytes.AsSpan(categories).StartsWith("categ:"u8) && bytes[^1] == 0, "the categ: record is not the end of the file");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
