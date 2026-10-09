using HolidayLights.App.Controls;

namespace HolidayLights.Tests.Ui;

/// <summary>The frame editor's rules and texts (PRODUCT-SPEC 3.2.2-3.2.7, Appendix D).</summary>
public sealed class ArrangementEditsTests
{
    private const string Standard = "builtin:standard-bulbs";
    private const string Snow = "builtin:snow-family";
    private const string Holly = "builtin:jolly-holly";
    private const string Candy = "builtin:candy-canes";

    private static readonly Func<string, string> NameOf = id => id switch
    {
        Standard => "Standard Bulbs",
        Snow => "Snow Family",
        Holly => "Jolly Holly",
        Candy => "Candy Canes",
        _ => id,
    };

    private static SlotAssignment Christmas1 => SlotAssignment.Classic54Default;

    [Fact]
    public void WholeFrameMakesEveryEdgeAndCornerTheBulb()
    {
        ArrangementEdit edit = ArrangementEdits.Use(Christmas1, ArrangementTarget.WholeFrame, Candy, NameOf);

        Assert.All(CellSlots.Sides, s => Assert.Equal([Candy], edit.Result.GetEdge(s)));
        Assert.All(CellSlots.Corners, c => Assert.Equal(Candy, edit.Result.GetCorner(c)));
        Assert.Equal("Use Candy Canes for the whole frame", edit.UndoText);
        Assert.Equal("Candy Canes now on the whole frame.", edit.Announcement);
        Assert.Equal("Using Candy Canes on the whole frame.", edit.Snackbar);
    }

    [Fact]
    public void AllEdgesKeepTheCorners()
    {
        ArrangementEdit edit = ArrangementEdits.Use(Christmas1, ArrangementTarget.AllEdges, Candy, NameOf);

        Assert.All(CellSlots.Sides, s => Assert.Equal([Candy], edit.Result.GetEdge(s)));
        Assert.All(CellSlots.Corners, c => Assert.Equal(Holly, edit.Result.GetCorner(c)));
        Assert.Equal("Using Candy Canes on all four edges.", edit.Snackbar);
    }

    [Fact]
    public void AllCornersKeepTheEdges()
    {
        ArrangementEdit edit = ArrangementEdits.Use(Christmas1, ArrangementTarget.AllCorners, Candy, NameOf);

        Assert.Equal(Christmas1.Right, edit.Result.Right);
        Assert.All(CellSlots.Corners, c => Assert.Equal(Candy, edit.Result.GetCorner(c)));
        Assert.Equal("Using Candy Canes in all four corners.", edit.Snackbar);
    }

    [Fact]
    public void AChipIsReplacedInPlace()
    {
        ArrangementEdit edit = ArrangementEdits.Use(Christmas1, ArrangementTarget.ForChip(Side.Right, 1), Candy, NameOf);

        Assert.Equal([Standard, Candy], edit.Result.Right);
        Assert.Equal("Snow Family replaced by Candy Canes on the right edge.", edit.Announcement);
        Assert.Null(edit.Snackbar);
    }

    [Fact]
    public void ThePlusAppendsAndMovesTheTargetToTheNewChip()
    {
        ArrangementEdit edit = ArrangementEdits.Use(Christmas1, ArrangementTarget.ForPlus(Side.Left), Snow, NameOf);

        Assert.Equal([Standard, Snow, Snow], edit.Result.Left);
        Assert.Equal(ArrangementTarget.ForChip(Side.Left, 2), edit.NextTarget);
        Assert.Equal("Added Snow Family to the left edge.", edit.Announcement);
    }

    [Fact]
    public void AFullEdgeRefusesMore()
    {
        SlotAssignment full = Christmas1.WithEdge(Side.Top, Enumerable.Repeat(Standard, 6));

        ArrangementEdit edit = ArrangementEdits.AddTo(full, Side.Top, [Candy], NameOf);

        Assert.Equal(full, edit.Result);
        Assert.Equal("This edge already has 6 bulb types. Remove one first.", edit.Problem);
    }

    [Fact]
    public void InsertFillsUpToSixThenStops()
    {
        SlotAssignment five = Christmas1.WithEdge(Side.Top, Enumerable.Repeat(Standard, 5));

        ArrangementEdit edit = ArrangementEdits.Insert(five, Side.Top, 2, [Candy, Snow], NameOf);

        Assert.Equal([Standard, Standard, Candy, Standard, Standard, Standard], edit.Result.Top);
        Assert.NotNull(edit.Problem);
    }

    [Fact]
    public void AddToEveryEdgeNamesTheFullEdges()
    {
        SlotAssignment fullBottom = Christmas1.WithEdge(Side.Bottom, Enumerable.Repeat(Snow, 6));

        ArrangementEdit edit = ArrangementEdits.AddToEveryEdge(fullBottom, [Candy], NameOf);

        Assert.Equal([Standard, Candy], edit.Result.Top);
        Assert.Equal(6, edit.Result.Bottom.Count);
        Assert.Equal("The bottom edge is full, so the bulb was not added there.", edit.Problem);
    }

    [Fact]
    public void BoxToBoxCopiesAndShiftMoves()
    {
        ArrangementEdit copy = ArrangementEdits.Transfer(Christmas1, BoxPosition.OnEdge(Side.Right, 1), BoxPosition.OnEdge(Side.Top, 1), replace: false, move: false, NameOf);
        ArrangementEdit move = ArrangementEdits.Transfer(Christmas1, BoxPosition.OnEdge(Side.Right, 1), BoxPosition.OnEdge(Side.Top, 1), replace: false, move: true, NameOf);

        Assert.Equal([Standard, Snow], copy.Result.Top);
        Assert.Equal([Standard, Snow], copy.Result.Right);
        Assert.Equal("Copied Snow Family to the top edge.", copy.Announcement);
        Assert.Equal([Standard, Snow], move.Result.Top);
        Assert.Equal([Standard], move.Result.Right);
        Assert.Equal("Moved Snow Family to the top edge.", move.Announcement);
    }

    [Fact]
    public void ACornerDropReplacesTheCorner()
    {
        ArrangementEdit edit = ArrangementEdits.Transfer(Christmas1, BoxPosition.OnEdge(Side.Top, 0), BoxPosition.InCorner(Corner.BottomRight), replace: false, move: false, NameOf);

        Assert.Equal(Standard, edit.Result.BottomRight);
        Assert.Equal([Standard], edit.Result.Top);
    }

    [Fact]
    public void DraggingWithinAnEdgeReorders()
    {
        ArrangementEdit edit = ArrangementEdits.Transfer(Christmas1, BoxPosition.OnEdge(Side.Bottom, 0), BoxPosition.OnEdge(Side.Bottom, 2), replace: false, move: false, NameOf);

        Assert.Equal([Holly, Snow], edit.Result.Bottom);
        Assert.Equal(ArrangementTarget.ForChip(Side.Bottom, 1), edit.NextTarget);
    }

    [Fact]
    public void DroppingOnAChipOfAFullEdgeReplacesIt()
    {
        SlotAssignment full = Christmas1.WithEdge(Side.Top, Enumerable.Repeat(Standard, 6));

        ArrangementEdit edit = ArrangementEdits.Transfer(full, BoxPosition.InCorner(Corner.TopLeft), BoxPosition.OnEdge(Side.Top, 3), replace: true, move: false, NameOf);

        Assert.Equal(Holly, edit.Result.Top[3]);
        Assert.Equal(6, edit.Result.Top.Count);
    }

    [Fact]
    public void RemoveMovesTheTargetToANeighbour()
    {
        ArrangementEdit edit = ArrangementEdits.Remove(Christmas1, Side.Right, 1, NameOf);

        Assert.Equal([Standard], edit.Result.Right);
        Assert.Equal("Removed Snow Family from the right edge.", edit.Announcement);
        Assert.Equal(ArrangementTarget.ForChip(Side.Right, 0), edit.NextTarget);
    }

    [Fact]
    public void ClearAllEmptiesEveryBox()
    {
        ArrangementEdit edit = ArrangementEdits.ClearAll(Christmas1);

        Assert.True(edit.Result.HasNoBulbs());
        Assert.Equal("Cleared all edges and corners.", edit.Snackbar);
    }

    [Fact]
    public void CopyEdgeToAllCopiesTheList()
    {
        ArrangementEdit edit = ArrangementEdits.CopyEdgeToAll(Christmas1, Side.Bottom);

        Assert.All(CellSlots.Sides, s => Assert.Equal([Snow, Holly], edit.Result.GetEdge(s)));
    }

    [Fact]
    public void WithoutTakesTheBulbOutOfEveryBox()
    {
        SlotAssignment result = ArrangementEdits.Without(Christmas1, Holly);

        Assert.Equal([Snow], result.Bottom);
        Assert.All(CellSlots.Corners, c => Assert.Null(result.GetCorner(c)));
        Assert.Equal(["Top Edge", "Right Edge", "Left Edge"], ArrangementEdits.PlacesOf(Christmas1, Standard));
    }

    [Theory]
    [InlineData(ArrangementTargetKind.WholeFrame, "the whole frame", "Use for the Whole Frame")]
    [InlineData(ArrangementTargetKind.AllEdges, "all four edges (corners stay)", "Use for All Edges")]
    [InlineData(ArrangementTargetKind.AllCorners, "all four corners", "Use in All Corners")]
    public void QuickTargetsHaveTheirSentences(ArrangementTargetKind kind, string sentence, string button)
    {
        var target = new ArrangementTarget(kind);

        Assert.Equal(sentence, target.Sentence(Christmas1));
        Assert.Equal(button, target.ButtonText());
    }

    [Fact]
    public void BoxTargetsHaveTheirSentences()
    {
        Assert.Equal("the 2nd bulb on the top edge", ArrangementTarget.ForChip(Side.Top, 1).Sentence(Christmas1));
        Assert.Equal("a new bulb at the end of the top edge (2 of 6)", ArrangementTarget.ForPlus(Side.Top).Sentence(Christmas1));
        Assert.Equal("the top-left corner", ArrangementTarget.ForCorner(Corner.TopLeft).Sentence(Christmas1));
        Assert.Equal("Use for the Top-Left Corner", ArrangementTarget.ForCorner(Corner.TopLeft).ButtonText());
        Assert.Equal("Add to the Bottom Edge", ArrangementTarget.ForPlus(Side.Bottom).ButtonText());
    }

    [Fact]
    public void TargetsFollowTheArrangement()
    {
        Assert.Equal(ArrangementTarget.ForPlus(Side.Top), ArrangementTarget.ForChip(Side.Top, 3).Normalize(Christmas1));
        SlotAssignment full = Christmas1.WithEdge(Side.Top, Enumerable.Repeat(Standard, 6));
        Assert.Equal(ArrangementTarget.ForChip(Side.Top, 5), ArrangementTarget.ForPlus(Side.Top).Normalize(full));
    }

    [Fact]
    public void TheSummaryReadsTheFrame()
    {
        Assert.Equal(
            "Top: Standard Bulbs. Right: Standard Bulbs, Snow Family. Bottom: Snow Family, Jolly Holly. Left: Standard Bulbs, Snow Family. Corners: Jolly Holly.",
            ArrangementTexts.Summary(Christmas1, NameOf));
        Assert.Equal("3rd", ArrangementTexts.Ordinal(3));
        Assert.Equal("11th", ArrangementTexts.Ordinal(11));
    }
}
