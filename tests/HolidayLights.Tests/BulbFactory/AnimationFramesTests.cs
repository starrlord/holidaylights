using HolidayLights.App.BulbFactory;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Core.Imaging;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>The "Animation Frames" group of Bulb Editing: 5.4 slot texts and flavors, Change, Remove Flavor, Copy to All Sides, the preview.</summary>
public sealed class AnimationFramesTests : IDisposable
{
    private readonly EditorTestBed bed = new();

    public void Dispose() => bed.Dispose();

    [Fact]
    public async Task OpensOnTheBulbListPreviewWithThe54Texts()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;

        Assert.Equal("Bulb List Preview", frames.SelectedSlot.Name);
        Assert.Equal(["Bulb List Preview", "Top Side", "Left Side", "Right Side", "Bottom Side", "Top-Left Corner", "Top-Right Corner", "Bottom-Left Corner", "Bottom-Right Corner"],
            frames.Slots.Select(s => s.Name));
        Assert.False(frames.IsSideSelected);
        Assert.True(frames.IsWhiteSquareVisible);
        Assert.Equal("This preview appears in the bulb list. Drag the white square to select the visible portion.", frames.SlotDescription);
        Assert.Equal("Press Change to choose a new animation for the bulb list preview.", frames.ChangeHint);
        Assert.All(frames.Flavors, f => Assert.EndsWith(" *", f.Label));
        Assert.Equal(CellSlot.Preview, frames.SelectedCell.Choice.Slot);
        Assert.Equal((1, 1), (frames.SelectedCell.Choice.Row, frames.SelectedCell.Choice.Column));
    }

    [Fact]
    public async Task SidesAndCornersExplainThemselvesLike54()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;

        frames.SelectedSlot = SlotChoice.For(CellSlot.TopRight);
        Assert.Equal("This animation appears in the corner of the screen. It has only one flavor.", frames.SlotDescription);
        Assert.Equal("Press Change to choose a new animation for the corner of the screen.", frames.ChangeHint);
        Assert.False(frames.CopyToAllSidesCommand.CanExecute(null));

        frames.SelectedSlot = SlotChoice.For(CellSlot.Top);
        Assert.Equal("This animation appears along the edge of the screen. It can have more than one flavor.", frames.SlotDescription);
        Assert.Equal("Press Change to choose a new animation.\r\n(The first flavor cannot be removed.)", frames.ChangeHint);
        Assert.Equal(["Flavor 1 *", "Flavor 2", "Flavor 3"], frames.Flavors.Take(3).Select(f => f.Label));
        Assert.False(frames.RemoveFlavorCommand.CanExecute(null));

        frames.SelectedFlavor = 1;
        Assert.Equal("Press Change to choose a new animation.", frames.ChangeHint);
        Assert.Equal(BulbFactoryStrings.NoAnimation, frames.PreviewMessage);
        Assert.Null(frames.Preview.CurrentFrame);
    }

    [Fact]
    public async Task Change_UsesAGifForTheSelectedFlavor_AndRemoveFlavorTakesItAway()
    {
        BulbEditingViewModel editor = await OpenAsync();
        AnimationFramesViewModel frames = editor.Frames;
        frames.SelectedSlot = SlotChoice.For(CellSlot.Left);
        frames.SelectedFlavor = 2;
        bed.Prompts.PictureFiles.Enqueue([bed.Scratch("blue.gif", SolidGif(16, 24, Blue))]);

        await frames.ChangeCommand.ExecuteAsync();
        await frames.WhenIdleAsync();

        Assert.Null(frames.ErrorMessage);
        Assert.Equal(SolidGif(16, 24, Blue), frames.Document.GetAnimation(CellSlot.Left, 2)!.Value.ToArray());
        Assert.Equal("Flavor 3 *", frames.Flavors[2].Label);
        Assert.Equal((16, 24), (frames.Preview.PictureWidth, frames.Preview.PictureHeight));
        Assert.Equal("Press Change to choose a new animation.\r\nPress Remove Flavor to delete this flavor.", frames.ChangeHint);
        Assert.True(editor.HasUnsavedChanges);

        frames.RemoveFlavorCommand.Execute(null);
        await frames.WhenIdleAsync();
        Assert.Null(frames.Document.GetGif(CellSlot.Left, 2));
        Assert.Equal("Flavor 3", frames.Flavors[2].Label);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Theory]
    [InlineData("broken.gif", "Cannot Import GIF File: A problem occurred when importing the GIF file. It cannot be used with this bulb.")]
    [InlineData("notes.txt", "Problem Importing File: Holiday Lights can use GIF files (.gif) and PNG pictures (.png) for bulbs.")]
    public async Task Change_ExplainsWhyAFileCannotBeUsed(string fileName, string message)
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;
        byte[] gif = SolidGif(8, 8, Red);
        string path = bed.Scratch(fileName, gif[..^1]);

        await frames.ImportFilesAsync([path]);

        Assert.Equal(message, frames.ErrorMessage);
        Assert.False(frames.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Change_TakesPngFramesInFileNameOrder()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;
        frames.SelectedSlot = SlotChoice.For(CellSlot.BottomLeft);
        string[] files =
        [
            TestPictures.WritePng(bed.Scratch("frame10.png", []), Solid(20, 10, Blue)),
            TestPictures.WritePng(bed.Scratch("frame2.png", []), Solid(20, 10, Green)),
            TestPictures.WritePng(bed.Scratch("frame1.png", []), Solid(20, 10, Red)),
        ];

        await frames.ImportFilesAsync(files);
        await frames.WhenIdleAsync();

        Assert.Null(frames.ErrorMessage);
        Assert.Equal("Frame 1 of 3", frames.Preview.FrameText);
        GifAnimation decoded = GifDecoder.DecodeClassic(frames.Document.GetAnimation(CellSlot.BottomLeft, 0)!.Value.Span);
        Assert.Equal([Red, Green, Blue], decoded.Frames.Select(f => f.Pixels[0]));
    }

    [Fact]
    public async Task Change_RefusesPngFramesOfDifferentSizesAndAGifWithPngs()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;
        string small = TestPictures.WritePng(bed.Scratch("a.png", []), Solid(8, 8, Red));
        string large = TestPictures.WritePng(bed.Scratch("b.png", []), Solid(9, 8, Red));

        await frames.ImportFilesAsync([small, large]);
        Assert.Equal("Cannot Import Picture: The pictures must all be the same size to become the frames of one animation.", frames.ErrorMessage);

        await frames.ImportFilesAsync([small, bed.Scratch("c.gif", SolidGif(8, 8, Red))]);
        Assert.Equal("Problem Importing File: Choose one GIF file, or one or more PNG pictures to use as the frames of one animation.", frames.ErrorMessage);
    }

    [Fact]
    public async Task CopyToAllSides_CanBeUndoneAndRedone()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;
        frames.SelectedSlot = SlotChoice.For(CellSlot.Right);
        await frames.ImportFilesAsync([bed.Scratch("green.gif", SolidGif(8, 8, Green))]);

        frames.CopyToAllSidesCommand.Execute(null);
        await frames.WhenIdleAsync();
        Assert.All(CellSlots.Sides, side => Assert.Equal(SolidGif(8, 8, Green), frames.Document.GetAnimation(side.ToSlot(), 0)!.Value.ToArray()));
        Assert.Equal("The other sides now use the flavors of the Right Side. Press Ctrl+Z to undo.", frames.NoticeMessage);

        frames.UndoCommand.Execute(null);
        Assert.NotEqual(SolidGif(8, 8, Green), frames.Document.GetAnimation(CellSlot.Top, 0)!.Value.ToArray());
        Assert.Equal(SolidGif(8, 8, Green), frames.Document.GetAnimation(CellSlot.Right, 0)!.Value.ToArray());

        frames.RedoCommand.Execute(null);
        Assert.Equal(SolidGif(8, 8, Green), frames.Document.GetAnimation(CellSlot.Top, 0)!.Value.ToArray());
        frames.UndoCommand.Execute(null);
        frames.UndoCommand.Execute(null);
        Assert.False(frames.UndoCommand.CanExecute(null));
        Assert.NotEqual(SolidGif(8, 8, Green), frames.Document.GetAnimation(CellSlot.Right, 0)!.Value.ToArray());
    }

    [Fact]
    public async Task TheWhiteSquare_MovesByKeysAndByDraggingInsideThePicture()
    {
        AnimationFramesViewModel frames = (await OpenAsync(SolidGif(100, 50, Red))).Frames;
        Assert.Equal(new RectI(34, 9, 66, 41), frames.WhiteSquare);

        Assert.True(frames.MoveWhiteSquareBy(8, 0));
        Assert.Equal(new RectI(42, 9, 74, 41), frames.WhiteSquare);
        frames.MoveWhiteSquareBy(100, 100);
        Assert.Equal(new RectI(68, 18, 100, 50), frames.WhiteSquare);

        frames.MoveWhiteSquareTo(20.5, 16);
        Assert.Equal(new RectI(4, 0, 36, 32), frames.WhiteSquare);

        frames.SelectedSlot = SlotChoice.For(CellSlot.Top);
        Assert.False(frames.IsWhiteSquareVisible);
        Assert.False(frames.MoveWhiteSquareBy(1, 0));
    }

    [Fact]
    public async Task ThePreview_StepsWithTheFlashClockAndThroughFramesWhilePaused()
    {
        AnimationFramesViewModel frames = (await OpenAsync(GifEncoder.Encode([Solid(4, 4, Red), Solid(4, 4, Green), Solid(4, 4, Blue)]))).Frames;
        frames.SelectedSlot = SlotChoice.For(CellSlot.Bottom);
        Assert.Equal("Frame 1 of 3", frames.Preview.FrameText);

        frames.Preview.Tick();
        Assert.Equal("Frame 2 of 3", frames.Preview.FrameText);
        Assert.False(frames.Preview.StepFrame(1));

        frames.Preview.IsPaused = true;
        frames.Preview.Tick();
        Assert.Equal("Frame 2 of 3", frames.Preview.FrameText);
        Assert.True(frames.Preview.StepFrame(-1));
        Assert.True(frames.Preview.StepFrame(-1));
        Assert.Equal("Frame 3 of 3", frames.Preview.FrameText);

        frames.SelectedSlot = SlotChoice.For(CellSlot.Preview);
        frames.Preview.IsPaused = false;
        frames.Preview.Tick();
        Assert.Equal("Frame 1 of 3", frames.Preview.FrameText);
    }

    [Fact]
    public async Task Zoom_FitsThePictureUntilTheUserChoosesOne()
    {
        AnimationFramesViewModel frames = (await OpenAsync(SolidGif(30, 20, Red))).Frames;

        frames.Preview.SetViewport(200, 120);
        Assert.Equal(5, frames.Preview.Zoom);
        frames.Preview.SetViewport(1000, 1000);
        Assert.Equal(8, frames.Preview.Zoom);

        frames.Preview.ZoomOutCommand.Execute(null);
        frames.Preview.SetViewport(200, 120);
        Assert.Equal(7, frames.Preview.Zoom);
        Assert.Equal("7x", frames.Preview.ZoomText);
    }

    [Fact]
    public async Task TheGifTip_ShowsForGifsThatLookDifferentElsewhere_UntilClosed()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;
        frames.SelectedSlot = SlotChoice.For(CellSlot.Top);
        Assert.False(frames.Preview.IsGifTipOpen);

        await frames.ImportFilesAsync([bed.Scratch("optimized.gif", TestPictures.OptimizedGif)]);
        await frames.WhenIdleAsync();
        Assert.True(frames.Preview.IsGifTipOpen);

        frames.Preview.IsGifTipOpen = false;
        frames.SelectedSlot = SlotChoice.For(CellSlot.Preview);
        frames.SelectedSlot = SlotChoice.For(CellSlot.Top);
        await frames.WhenIdleAsync();
        Assert.False(frames.Preview.IsGifTipOpen);
    }

    [Fact]
    public async Task TheSlotMap_ShowsEachSlotAndMovesLikeTheScreen()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;

        Assert.All(frames.SlotMap, cell => Assert.NotNull(cell.Thumbnail));
        frames.MoveSlotSelection(-1, -1);
        Assert.Equal(CellSlot.TopLeft, frames.SelectedSlot.Slot);
        frames.MoveSlotSelection(0, 1);
        Assert.Equal(CellSlot.Top, frames.SelectedSlot.Slot);
        frames.MoveSlotSelection(-1, 0);
        Assert.Equal(CellSlot.Top, frames.SelectedSlot.Slot);
        frames.SelectedCell = frames.SlotMap[8];
        Assert.Equal(CellSlot.BottomRight, frames.SelectedSlot.Slot);
        Assert.Equal(Side.Bottom, frames.EdgeSampleSide);
        Assert.True(frames.EdgeSampleIncludesCorners);
    }

    [Fact]
    public async Task TheEdgeSample_ShowsTheUnsavedDocument()
    {
        AnimationFramesViewModel frames = (await OpenAsync()).Frames;
        frames.SelectedSlot = SlotChoice.For(CellSlot.Top);

        await frames.ImportFilesAsync([bed.Scratch("blue.gif", SolidGif(6, 6, Blue))]);
        await frames.WhenIdleAsync();

        Assert.True(frames.EdgeSampleBulbs!.TryGetBulb(frames.EdgeSampleBulbId, out IBulb? bulb));
        Assert.Equal(Blue, bulb.GetCell(CellSlot.Top, 0, 0).Image.Pixels[0]);
        Assert.True(bed.Catalog.TryGetBulb(frames.EdgeSampleBulbId, out IBulb? saved));
        Assert.NotEqual(Blue, saved.GetCell(CellSlot.Top, 0, 0).Image.Pixels[0]);
        Assert.NotEqual(saved.ContentKey, bulb.ContentKey);
    }

    private async Task<BulbEditingViewModel> OpenAsync(byte[]? gif = null) =>
        await bed.OpenAsync(bed.AddMyBulb(Document(gif ?? NumberedGif(1), "Star")));
}
