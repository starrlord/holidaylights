using System.Buffers.Binary;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Tests.Shared;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

public sealed class BulFileWriterTests
{
    [Fact]
    public void Encode_WritesThe54HeaderLayout()
    {
        byte[] top = NumberedGif(1);
        byte[] flavor = NumberedGif(2);
        BulbDocument document = Document(top, "Star");
        document.SetAnimation(CellSlot.Top, 1, flavor);
        document.SetAnimation(CellSlot.Left, 0, flavor);
        document.Categories.Add("Christmas");
        document.Categories.Add("Winter");

        byte[] bytes = BulFileWriter.Encode(document, 0x01234567).Bytes;

        Assert.Equal("bluBrgiT"u8.ToArray(), bytes[..8]);
        Assert.Equal(4, Int(bytes, 0x08));
        Assert.Equal(0x01234567, Int(bytes, 0x0C));
        Assert.Equal([0, 0, 0], new[] { Int(bytes, 0x10), Int(bytes, 0x14), Int(bytes, 0x18) });
        Assert.Equal("Star\0"u8.ToArray(), bytes[0x1C..0x21]);
        Assert.All(bytes[0x21..0x6C], b => Assert.Equal(0, b));
        Assert.Equal((0, 0), (Int(bytes, 0x15C), Int(bytes, 0x160)));
        Assert.Equal(0, Int(bytes, 0x164));
        Assert.Equal(new[] { 0, 0, 0, 0 }, Ints(bytes, 0x168, 4));
        Assert.Equal(new[] { 0, 1, -1, -1, -1, -1, -1, -1 }, Ints(bytes, 0x178, 8));
        Assert.Equal(new[] { 1, -1, -1, -1, -1, -1, -1, -1 }, Ints(bytes, 0x1D8, 8));

        Assert.Equal(new[] { top.Length, 0x57C, unchecked((int)ReferenceCrc(top)), 8, 1 }, Ints(bytes, 0x1F8, 5));
        Assert.Equal(new[] { flavor.Length, 0x57C + top.Length, unchecked((int)ReferenceCrc(flavor)), 2, 1 }, Ints(bytes, 0x20C, 5));
        Assert.All(bytes[0x220..0x574], b => Assert.Equal(0, b));
        Assert.Equal(top, bytes[0x57C..(0x57C + top.Length)]);

        int categories = Int(bytes, 0x574);
        Assert.Equal(0x57C + top.Length + flavor.Length, categories);
        Assert.Equal("categ:Christmas|Winter\0"u8.ToArray(), bytes[categories..]);
        Assert.Equal(bytes.Length, Int(bytes, 0x578));
    }

    [Fact]
    public void Encode_CutsLongTextsAndReportsReplacedCharacters()
    {
        BulbDocument document = Document(NumberedGif(1));
        document.Name = new string('n', 100);
        document.Author = "Zoë 张\r\nzoe@example.com";
        document.Categories.Add("Snow ☃");

        BulFileContent content = BulFileWriter.Encode(document, 50);
        BulFile file = BulFile.Parse(content.Bytes);

        Assert.True(content.CharactersReplaced);
        Assert.Equal(new string('n', 79), file.Name);
        Assert.Equal("Zoë ?\r\nzoe@example.com", file.Author);
        Assert.Equal(["Snow ?"], file.Categories);
        Assert.False(BulFileWriter.Encode(Document(NumberedGif(1), "Zoë"), 50).CharactersReplaced);
    }

    [Fact]
    public void Encode_KeepsCategoriesThat54CanLoad()
    {
        BulbDocument document = Document(NumberedGif(1));
        foreach (int i in Enumerable.Range(0, 30))
        {
            document.Categories.Add($"Category {i:00}");
        }

        BulFile file = BulFile.Parse(BulFileWriter.Encode(document, 50).Bytes);

        // 11 characters each: 21 names and 20 bars make 251 characters; a 22nd would exceed 255.
        Assert.False(file.IsDamaged);
        Assert.Equal(document.Categories.Take(21), file.Categories);
    }

    [Fact]
    public void Encode_NeedsANameAndEveryRequiredSlot()
    {
        BulbDocument nameless = Document(NumberedGif(1));
        nameless.Name = "  ";

        Assert.Throws<ArgumentException>(() => BulFileWriter.Encode(nameless, 50));
        Assert.Throws<ArgumentException>(() => BulFileWriter.Encode(new BulbDocument { Name = "Empty" }, 50));
    }

    [Fact]
    public void WriteAtomically_CreatesAndReplacesWithoutLeavingATemporaryFile()
    {
        using var root = new TempDataRoot();
        string path = Path.Combine(root.Root, "Star.bul");
        BulFileContent first = BulFileWriter.Encode(Document(NumberedGif(1), "Star"), 50);
        BulFileContent second = BulFileWriter.Encode(Document(NumberedGif(2), "Star"), 51);

        BulFileWriter.WriteAtomically(path, first);
        Assert.Equal(first.Bytes, File.ReadAllBytes(path));
        BulFileWriter.WriteAtomically(path, second);

        Assert.Equal(second.Bytes, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFiles(root.Root));
    }

    [Fact]
    public void WriteAtomically_LeavesTheTargetAloneWhenItCannotBeReplaced()
    {
        using var root = new TempDataRoot();
        string path = Path.Combine(root.Root, "Star.bul");
        Directory.CreateDirectory(path);

        Exception error = Assert.ThrowsAny<Exception>(() => BulFileWriter.WriteAtomically(path, BulFileWriter.Encode(Document(NumberedGif(1)), 50)));

        Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());

        Assert.True(Directory.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteTemporaryFile_WritesNextToTheTarget()
    {
        using var root = new TempDataRoot();
        string path = Path.Combine(root.Root, "Star.bul");
        BulFileContent content = BulFileWriter.Encode(Document(NumberedGif(1)), 50);

        string temporary = BulFileWriter.WriteTemporaryFile(path, content);

        Assert.Equal(path + ".tmp", temporary);
        Assert.Equal(content.Bytes, File.ReadAllBytes(temporary));
        Assert.False(File.Exists(path));
    }

    private static int Int(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));

    private static int[] Ints(byte[] bytes, int offset, int count) => [.. Enumerable.Range(0, count).Select(i => Int(bytes, offset + 4 * i))];
}
