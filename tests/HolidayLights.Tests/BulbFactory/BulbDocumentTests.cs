using System.Buffers.Binary;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

public sealed class BulbDocumentTests
{
    [Fact]
    public void NewDocument_HasNoAnimationsAndMissesEveryRequiredSlot()
    {
        var document = new BulbDocument();

        Assert.Equal(0, document.AnimationCount);
        Assert.False(document.IsComplete);
        Assert.Equal(9, document.MissingSlots.Count());
        Assert.Null(document.GetAnimation(CellSlot.Top, 0));
    }

    [Fact]
    public void IdenticalGifs_AreStoredOnce()
    {
        BulbDocument document = Document(NumberedGif(1));

        Assert.True(document.SetAnimation(CellSlot.Top, 1, NumberedGif(2)));
        Assert.True(document.SetAnimation(CellSlot.Left, 3, NumberedGif(2)));
        Assert.True(document.SetAnimation(CellSlot.TopLeft, 0, NumberedGif(1)));

        Assert.Equal(2, document.AnimationCount);
        Assert.Same(document.GetGif(CellSlot.Top, 1), document.GetGif(CellSlot.Left, 3));
        Assert.Equal(NumberedGif(2), document.GetAnimation(CellSlot.Left, 3)!.Value.ToArray());
    }

    [Fact]
    public void ABulbWith37DifferentGifs_FillsEveryEntryAndStillTakesANewGif()
    {
        BulbDocument document = Document(NumberedGif(0));
        int number = 1;
        foreach (Side side in CellSlots.Sides)
        {
            for (int flavor = 0; flavor < BulbDocument.MaxFlavors; flavor++)
            {
                Assert.True(document.SetAnimation(side.ToSlot(), flavor, NumberedGif(number++)));
            }
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            Assert.True(document.SetAnimation(corner.ToSlot(), 0, NumberedGif(number++)));
        }

        Assert.Equal(BulbDocument.MaxAnimations, document.AnimationCount);
        Assert.True(document.SetAnimation(CellSlot.BottomRight, 0, NumberedGif(number)));
        Assert.Equal(BulbDocument.MaxAnimations, document.AnimationCount);
        Assert.Equal(NumberedGif(number), document.GetAnimation(CellSlot.BottomRight, 0)!.Value.ToArray());

        BulFile file = BulFile.Parse(BulFileWriter.Encode(document, 99).Bytes);
        Assert.False(file.IsDamaged);
        Assert.All(file.Entries, e => Assert.Equal(1, e.RefCount));
    }

    [Fact]
    public void RemoveFlavor_LeavesAGapThatTheFlavorsCloseUpOnSave()
    {
        BulbDocument document = Document(NumberedGif(0));
        document.SetAnimation(CellSlot.Top, 1, NumberedGif(1));
        document.SetAnimation(CellSlot.Top, 2, NumberedGif(2));

        document.RemoveFlavor(Side.Top, 1);

        Assert.Null(document.GetGif(CellSlot.Top, 1));
        Assert.Equal(2, document.GetFlavorCount(Side.Top));
        Assert.Equal([NumberedGif(0), NumberedGif(2)], document.GetFlavors(Side.Top).Select(g => g.Bytes.ToArray()));
        Assert.Equal(2, document.AnimationCount);
        BulFile file = BulFile.Parse(BulFileWriter.Encode(document, 99).Bytes);
        Assert.Equal(new[] { 0, 1, -1, -1, -1, -1, -1, -1 }, file.SideEntries[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.RemoveFlavor(Side.Top, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.RemoveFlavor(Side.Top, 8));
    }

    [Fact]
    public void CopyToAllSides_GivesTheOtherSidesThisSidesFlavors()
    {
        BulbDocument document = Document(NumberedGif(0));
        document.SetAnimation(CellSlot.Left, 0, NumberedGif(1));
        document.SetAnimation(CellSlot.Left, 1, NumberedGif(2));
        document.SetAnimation(CellSlot.Right, 0, NumberedGif(3));

        document.CopyToAllSides(Side.Left);

        foreach (Side side in CellSlots.Sides)
        {
            Assert.Equal([NumberedGif(1), NumberedGif(2)], document.GetFlavors(side).Select(g => g.Bytes.ToArray()));
        }

        Assert.Equal(3, document.AnimationCount);
        Assert.DoesNotContain(document.Gifs, g => g.Bytes.Span.SequenceEqual(NumberedGif(3)));
    }

    [Fact]
    public void ANewPreviewAnimation_CentresTheWhiteSquare_ThatStaysInsideThePicture()
    {
        BulbDocument document = Document(SolidGif(100, 41, Red));
        Assert.Equal(new RectI(34, 4, 66, 36), document.PreviewWindow);

        document.MovePreviewWindow(90, -5);
        Assert.Equal((68, 0), (document.PreviewX, document.PreviewY));

        document.SetAnimation(CellSlot.Preview, 0, SolidGif(20, 64, Green));
        Assert.Equal(new RectI(0, 16, 20, 48), document.PreviewWindow);

        document.PreviewX = 500;
        Assert.Equal(new RectI(0, 16, 20, 48), document.PreviewWindow);
    }

    [Fact]
    public void Clone_IsIndependentAndHasTheSameContent()
    {
        BulbDocument document = Document(NumberedGif(0));
        document.Categories.Add("Christmas");
        BulbDocument copy = document.Clone();

        Assert.True(copy.HasSameContent(document));

        copy.SetAnimation(CellSlot.Bottom, 2, NumberedGif(7));
        copy.Categories.Add("Winter");
        Assert.False(copy.HasSameContent(document));
        Assert.Null(document.GetGif(CellSlot.Bottom, 2));
        Assert.Equal(["Christmas"], document.Categories);
    }

    [Fact]
    public void HasSameContent_IgnoresGapsButNotTexts()
    {
        BulbDocument document = Document(NumberedGif(0));
        document.SetAnimation(CellSlot.Top, 2, NumberedGif(1));
        BulbDocument packed = Document(NumberedGif(0));
        packed.SetAnimation(CellSlot.Top, 1, NumberedGif(1));

        Assert.True(document.HasSameContent(packed));
        packed.Description = "Changed";
        Assert.False(document.HasSameContent(packed));
    }

    [Fact]
    public void FromFile_TakesWhat54DrawsAndDropsTheInternalCategories()
    {
        byte[] first = NumberedGif(1);
        byte[] second = NumberedGif(2);
        byte[] data = new Bulbs.TestBul
        {
            Gifs = [first, second, first],
            Locked = false,
            PreviewEntry = 2,
            Corners = [0, 1, 2, 0],
            Sides = [[1, 0], [0], [2], [0]],
            Categories = "Christmas|_1|Winter",
            PreviewX = 5,
            PreviewY = 7,
        }.Build();
        Span<byte> top = data.AsSpan(0x178);
        BinaryPrimitives.WriteInt32LittleEndian(top[8..], -1);
        BinaryPrimitives.WriteInt32LittleEndian(top[12..], 1);

        BulbDocument document = BulbDocument.FromFile(BulFile.Parse(data));

        Assert.Equal(["Christmas", "Winter"], document.Categories);
        Assert.Equal(2, document.AnimationCount);
        Assert.Equal(first, document.Gifs[0].Bytes.ToArray());
        Assert.Same(document.GetGif(CellSlot.Preview, 0), document.GetGif(CellSlot.TopLeft, 0));
        Assert.Equal(2, document.GetFlavorCount(Side.Top));
        Assert.Equal((5, 7), (document.PreviewX, document.PreviewY));
        Assert.Equal("Test Bulb", document.Name);
        Assert.True(document.IsComplete);
    }

    [Fact]
    public void FromFile_LeavesASlotWithAnEmptyEntryEmptyAndRefusesDamagedFiles()
    {
        byte[] data = new Bulbs.TestBul { Gifs = [NumberedGif(1)], Corners = [0, 0, 5, 0] }.Build();

        BulbDocument document = BulbDocument.FromFile(BulFile.Parse(data));

        Assert.Equal([CellSlot.BottomRight], document.MissingSlots);
        Assert.Throws<ArgumentException>(() => BulbDocument.FromFile(BulFile.Parse(data.AsSpan(0, 100).ToArray())));
    }

    [Fact]
    public void SetAnimation_RefusesWhatTheFaithfulDecoderRefuses()
    {
        BulbDocument document = Document(NumberedGif(1));
        byte[] gif = NumberedGif(2);

        Assert.Throws<InvalidDataException>(() => document.SetAnimation(CellSlot.Top, 0, gif.AsMemory(0, gif.Length - 1)));
        Assert.Throws<InvalidDataException>(() => document.SetAnimation(CellSlot.Top, 0, "not a gif;"u8.ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetAnimation(CellSlot.Top, 8, gif));
        Assert.Equal(1, document.AnimationCount);
    }

    [Fact]
    public void ConstantMask_IsWorkedOutFromTheFrames()
    {
        Rgba32Image shape = Solid(4, 4, Red);
        Rgba32Image other = Solid(4, 4, Blue);
        other.Pixels[5] = 0;

        Assert.True(BulbGif.FromBytes(GifEncoder.Encode([shape, Solid(4, 4, Blue)])).HasConstantMask);
        Assert.False(BulbGif.FromBytes(GifEncoder.Encode([shape, other])).HasConstantMask);
    }
}
