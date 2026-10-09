using System.Buffers.Binary;
using HolidayLights.Core.Bulbs;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

public sealed class BulFileTests
{
    private static readonly byte[] Gif = TestGif.Encode(2, 2, [(0, 0, 0), (255, 0, 0)], [new TestGifFrame { Width = 2, Height = 2, Pixels = [1, 0, 0, 1] }]);
    private static readonly byte[] OtherGif = TestGif.Encode(2, 2, [(0, 0, 0), (0, 0, 255)], [new TestGifFrame { Width = 2, Height = 2, Pixels = [1, 1, 0, 1] }]);

    [Fact]
    public void Parse_ReadsTheHeaderFields()
    {
        byte[] data = new TestBul { Gifs = [Gif, OtherGif], BulbId = 0xDEADBEEF, Locked = false, PreviewX = 3, PreviewY = 4, Sides = [[0, 1], [1], [0], [1, 0, 1]] }.Build();

        BulFile file = BulFile.Parse(data, "x.bul");

        Assert.False(file.IsDamaged, string.Join("; ", file.Problems));
        Assert.Equal(unchecked((int)0xDEADBEEF), file.LegacyId);
        Assert.False(file.Locked);
        Assert.False(file.Unsent);
        Assert.Equal("Test Bulb", file.Name);
        Assert.Equal("A bulb for tests.", file.Description);
        Assert.Equal("Copyright 2026 Tester", file.Copyright);
        Assert.Equal("Tester\r\ntester@example.com", file.Author);
        Assert.Equal((3, 4), (file.PreviewX, file.PreviewY));
        Assert.Equal(new[] { "Christmas", "Winter" }, file.Categories);
        Assert.Equal(data.Length, file.RecordedFileSize);
        Assert.Equal(2, file.GetFlavorCount(Side.Top));
        Assert.Equal(3, file.GetFlavorCount(Side.Left));
        Assert.Equal(37, file.Entries.Count);
        Assert.Equal(Gif, file.Entries[0].Gif.ToArray());
        Assert.Equal(TestBul.Crc32Bzip2(OtherGif), file.Entries[1].Crc);
        Assert.Equal(0, file.Entries[2].Size);
        Assert.True(file.Entries[2].Gif.IsEmpty);
    }

    [Fact]
    public void GetEntryIndex_FollowsTheSlotTableWithFlavorsModuloTheFlavorCount()
    {
        BulFile file = BulFile.Parse(new TestBul { Gifs = [Gif, OtherGif], PreviewEntry = 1, Corners = [0, 1, 0, 1], Sides = [[0, 1], [1], [0], [1]] }.Build());

        Assert.Equal(1, file.GetEntryIndex(CellSlot.Preview, 5));
        Assert.Equal(1, file.GetEntryIndex(CellSlot.TopRight, 0));
        Assert.Equal(0, file.GetEntryIndex(CellSlot.BottomRight, 3));
        Assert.Equal(0, file.GetEntryIndex(CellSlot.Top, 2));
        Assert.Equal(1, file.GetEntryIndex(CellSlot.Top, 3));
        Assert.Equal(1, file.GetEntryIndex(CellSlot.Top, -1));
        Assert.Equal(1, file.GetEntryIndex(CellSlot.Right, 6));
    }

    [Fact]
    public void FlavorCount_StopsAtTheFirstUnusedFlavor()
    {
        byte[] data = new TestBul { Gifs = [Gif, OtherGif] }.Build();
        Span<byte> top = data.AsSpan(0x178);
        BinaryPrimitives.WriteInt32LittleEndian(top, 0);
        BinaryPrimitives.WriteInt32LittleEndian(top[4..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(top[8..], -1);
        BinaryPrimitives.WriteInt32LittleEndian(top[12..], 1);

        Assert.Equal(2, BulFile.Parse(data).GetFlavorCount(Side.Top));
    }

    [Fact]
    public void Strings_IgnoreStaleBytesAndRunOnWithoutATerminator()
    {
        byte[] data = new TestBul { Gifs = [Gif], Description = "Short" }.Build();
        "Short\0stale tail"u8.CopyTo(data.AsSpan(0x6C));
        data.AsSpan(0x1C, 80).Fill((byte)'A');

        BulFile file = BulFile.Parse(data);

        Assert.Equal("Short", file.Description);
        Assert.Equal(new string('A', 80) + "Short", file.Name);
    }

    [Fact]
    public void Strings_AreWindows1252()
    {
        byte[] data = new TestBul { Gifs = [Gif] }.Build();
        byte[] name = [(byte)'S', (byte)'t', (byte)'j', 0xE4, (byte)'r', (byte)'n', (byte)'a', 0x80, 0x81, 0];
        name.CopyTo(data.AsSpan(0x1C));

        Assert.Equal("Stj" + (char)0xE4 + "rna" + (char)0x20AC + (char)0x81, BulFile.Parse(data).Name);
    }

    [Theory]
    [InlineData("A||B", new[] { "A", "B" })]
    [InlineData(" Spaced  | X ", new[] { "Spaced", "X" })]
    [InlineData("_1|Real", new[] { "_1", "Real" })]
    [InlineData("", new string[0])]
    public void Categories_AreSplitTrimmedAndEmptiesDropped(string record, string[] expected)
    {
        BulFile file = BulFile.Parse(new TestBul { Gifs = [Gif], Categories = record }.Build());

        Assert.False(file.IsDamaged);
        Assert.Equal(expected, file.Categories);
    }

    [Fact]
    public void Categories_NeedTheRecordPrefix()
    {
        byte[] data = new TestBul { Gifs = [Gif], Categories = "Christmas" }.Build();
        int offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x574));
        "xateg:"u8.CopyTo(data.AsSpan(offset));

        BulFile file = BulFile.Parse(data);

        Assert.False(file.IsDamaged);
        Assert.Empty(file.Categories);
    }

    [Fact]
    public void Problems_FollowThe54LoaderRules()
    {
        byte[] good = new TestBul { Gifs = [Gif], RecordSize = false }.Build();

        AssertDamaged(good.AsSpan(0, 100).ToArray(), "shorter");
        AssertDamaged(Patched(good, d => d[0] = (byte)'X'), "not a Holiday Lights bulb file");
        AssertDamaged(Patched(good, d => BinaryPrimitives.WriteInt32LittleEndian(d[0x164..], -1)), "bulb list preview");
        AssertDamaged(Patched(good, d => BinaryPrimitives.WriteInt32LittleEndian(d[0x170..], -1)), "bottom-right corner");
        AssertDamaged(new TestBul { Gifs = [Gif], Sides = [[0], [], [0], [0]] }.Build(), "right side");
        AssertDamaged(new TestBul { Gifs = [Gif], Trailing = [1, 2, 3] }.Build(), "should be");
        AssertDamaged(Patched(good, d => d[0x57C + Gif.Length - 1] = 0), "incomplete");
        AssertDamaged(Patched(good, d => d[0x1F8..0x4DC].Clear()), "no animations");
        AssertDamaged(new TestBul { Gifs = [Gif], Categories = new string('x', 256) }.Build(), "longer than 255");
        AssertDamaged(new TestBul { Gifs = [Gif], Categories = "Fine|" + new string('y', 40) }.Build(), "longer than 39");
        AssertDamaged(Patched(good, d => BinaryPrimitives.WriteInt32LittleEndian(d[0x574..], d.Length + 10)), "category list is missing");
        Assert.False(BulFile.Parse(good).IsDamaged);
        Assert.False(BulFile.Parse(new TestBul { Gifs = [Gif], Categories = new string('x', 39) }.Build()).IsDamaged);
    }

    [Fact]
    public void Problems_ListEveryReason()
    {
        byte[] data = new TestBul { Gifs = [Gif], PreviewEntry = -1, Corners = [-1, 0, 0, -1] }.Build();

        BulFile file = BulFile.Parse(data);

        Assert.Equal(3, file.Problems.Count);
    }

    [Fact]
    public void Read_ReportsUnreadableFilesAsIOException()
    {
        using var root = new TempDataRoot();

        Assert.ThrowsAny<IOException>(() => BulFile.Read(Path.Combine(root.Root, "missing.bul")));
        Assert.ThrowsAny<IOException>(() => BulFile.Read(root.Root));
    }

    [Fact]
    public void Read_KeepsThePath()
    {
        using var root = new TempDataRoot();
        string path = new TestBul { Gifs = [Gif] }.WriteTo(Path.Combine(root.Root, "x.bul"));

        Assert.Equal(path, BulFile.Read(path).FilePath);
        Assert.Null(BulFile.Parse(File.ReadAllBytes(path)).FilePath);
    }

    [Fact]
    public void ContentIdentity_IgnoresIdFlagsCategoriesAndAppendedData()
    {
        string identity = Identity(new TestBul { Gifs = [Gif, OtherGif], Sides = [[0, 1], [0], [1], [0]] });

        Assert.Equal(identity, Identity(new TestBul
        {
            Gifs = [Gif, OtherGif], Sides = [[0, 1], [0], [1], [0]], BulbId = 99, Locked = false, Categories = "Other",
            Trailing = [0x6E, 0x67, 0x65, 0x52, 1, 2, 3, 4], RecordSize = false,
        }));

        // The same GIFs in the other order in the entry table, with the slot table pointing at them accordingly.
        Assert.Equal(identity, Identity(new TestBul
        {
            Gifs = [OtherGif, Gif], PreviewEntry = 1, Corners = [1, 1, 1, 1], Sides = [[1, 0], [1], [0], [1]],
        }));
    }

    [Fact]
    public void ContentIdentity_ChangesWithTextPreviewSlotsAndArt()
    {
        string identity = Identity(new TestBul { Gifs = [Gif, OtherGif], Sides = [[0, 1], [0], [1], [0]] });

        Assert.NotEqual(identity, Identity(new TestBul { Gifs = [Gif, OtherGif], Sides = [[0, 1], [0], [1], [0]], Name = "Other" }));
        Assert.NotEqual(identity, Identity(new TestBul { Gifs = [Gif, OtherGif], Sides = [[0, 1], [0], [1], [0]], Description = "x" }));
        Assert.NotEqual(identity, Identity(new TestBul { Gifs = [Gif, OtherGif], Sides = [[0, 1], [0], [1], [0]], PreviewX = 1 }));
        Assert.NotEqual(identity, Identity(new TestBul { Gifs = [Gif, OtherGif], Sides = [[1, 0], [0], [1], [0]] }));
        Assert.NotEqual(identity, Identity(new TestBul { Gifs = [Gif, Gif], Sides = [[0, 1], [0], [1], [0]] }));
        Assert.Matches("^[0-9a-f]{64}$", identity);
    }

    [Fact]
    public void ContentIdentity_IsEqualForTheSameBundledBulbFromMemoryAndFromDisk()
    {
        string path = Path.Combine(BulbGoldens.BundledBulbsFolder, "Arrow.bul");

        Assert.Equal(BulFile.Read(path).ComputeContentIdentity(), BulFile.Parse(File.ReadAllBytes(path)).ComputeContentIdentity());
    }

    private static string Identity(TestBul bul) => BulFile.Parse(bul.Build()).ComputeContentIdentity();

    private static byte[] Patched(byte[] data, SpanAction patch)
    {
        byte[] copy = (byte[])data.Clone();
        patch(copy);
        return copy;
    }

    private static void AssertDamaged(byte[] data, string reason)
    {
        BulFile file = BulFile.Parse(data);
        Assert.True(file.IsDamaged);
        Assert.Contains(file.Problems, p => p.Contains(reason, StringComparison.OrdinalIgnoreCase));
    }

    private delegate void SpanAction(Span<byte> data);
}
