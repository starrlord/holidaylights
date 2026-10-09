using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace HolidayLights.App.Controls;

/// <summary>Drag and drop in the frame editor (PRODUCT-SPEC 3.2.7).</summary>
public partial class ArrangementEditor
{
    private Point? dragStart;
    private bool dropTargetsShown;
    private bool filesShowTargets;

    /// <summary>
    /// Shows (or hides) the dashed accent outline on every box while a bulb is dragged anywhere in the window (the Bulb
    /// List calls this around its drags; chip drags do it themselves).
    /// </summary>
    /// <param name="show">True while a drag runs.</param>
    public void ShowDropTargets(bool show)
    {
        dropTargetsShown = show;
        ClearDragMarks();
    }

    /// <summary>Removes the insertion bars, "Replace" marks and fills; keeps the outlines while a drag runs.</summary>
    private void ClearDragMarks()
    {
        foreach (ArrangementChip chip in AllChips())
        {
            chip.Insertion = InsertionMark.None;
            chip.IsReplaceTarget = false;
        }

        foreach (BoxBase box in Boxes())
        {
            box.ShowDragState(dropTargetsShown, over: false, refused: false);
        }

        Stage.HighlightedSlot = hoverSlot;
    }

    private IEnumerable<BoxBase> Boxes() => edges.Values.Cast<BoxBase>().Concat(corners.Values);

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private static string[] FilesOf(DragEventArgs e) => e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];

    /// <summary>Copy, or Move with Shift when the bulb comes from a box.</summary>
    private static DragDropEffects EffectFor(BulbDragData data, DragEventArgs e) =>
        data.Source is not null && (e.KeyStates & DragDropKeyStates.ShiftKey) != 0 ? DragDropEffects.Move : DragDropEffects.Copy;

    /// <summary>Over the editor but not over a box or the stage (the gaps): nothing happens there.</summary>
    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (BulbDrag.From(e.Data) is { } data)
        {
            data.OverFrame = true;
            data.Hint = null;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnEditorDragEnter(object sender, DragEventArgs e)
    {
        if (!dropTargetsShown && HasFiles(e))
        {
            dropTargetsShown = true;
            filesShowTargets = true;
            ClearDragMarks();
        }
    }

    private void OnEditorDragLeave(object sender, DragEventArgs e)
    {
        Point position = e.GetPosition(this);
        if (position.X >= 0 && position.Y >= 0 && position.X < ActualWidth && position.Y < ActualHeight)
        {
            return;
        }

        if (BulbDrag.From(e.Data) is { } data)
        {
            data.OverFrame = false;
            data.Hint = null;
        }

        if (filesShowTargets)
        {
            filesShowTargets = false;
            dropTargetsShown = false;
        }

        ClearDragMarks();
    }

    /// <summary>The insertion position in an edge for the pointer (by chip centers).</summary>
    private static int InsertionIndex(EdgeBox box, DragEventArgs e)
    {
        bool vertical = box.Side is Side.Left or Side.Right;
        int index = 0;
        foreach (ArrangementChip chip in box.Chips)
        {
            Point p = e.GetPosition(chip);
            double offset = vertical ? p.Y - chip.ActualHeight / 2 : p.X - chip.ActualWidth / 2;
            if (offset > 0)
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>What a drop on an edge would do, and the marks that show it.</summary>
    private EdgeDrop PlanEdgeDrop(EdgeBox box, BulbDragData data, DragEventArgs e)
    {
        int count = Arrangement.GetEdge(box.Side).Count;
        int insert = InsertionIndex(box, e);
        ArrangementChip? over = e.OriginalSource is DependencyObject source ? box.FindChip(source) : null;
        int overIndex = over is null ? -1 : box.IndexOfChip(over);
        if (data.Source is { Side: { } from } && from == box.Side)
        {
            return new EdgeDrop(EdgeDropKind.Reorder, insert);
        }

        if (count >= SlotAssignment.MaxTypesPerEdge)
        {
            return overIndex >= 0 ? new EdgeDrop(EdgeDropKind.Replace, overIndex) : new EdgeDrop(EdgeDropKind.Refused, -1);
        }

        return new EdgeDrop(EdgeDropKind.Insert, insert);
    }

    private void ShowEdgeDrop(EdgeBox box, EdgeDrop drop)
    {
        ClearDragMarks();
        switch (drop.Kind)
        {
            case EdgeDropKind.Insert or EdgeDropKind.Reorder:
                if (drop.Index < box.Chips.Count)
                {
                    box.Chips[drop.Index].Insertion = InsertionMark.Before;
                }
                else if (box.Chips.Count > 0)
                {
                    box.Chips[^1].Insertion = InsertionMark.After;
                }

                break;
            case EdgeDropKind.Replace:
                box.Chips[drop.Index].IsReplaceTarget = true;
                break;
        }

        box.ShowDragState(dropTargetsShown, over: true, refused: drop.Kind == EdgeDropKind.Refused);
        Stage.HighlightedSlot = box.Slot;
    }

    private void OnEdgeDragOver(EdgeBox box, DragEventArgs e)
    {
        e.Handled = true;
        if (BulbDrag.From(e.Data) is { } data)
        {
            EdgeDrop drop = PlanEdgeDrop(box, data, e);
            data.OverFrame = true;
            data.Hint = drop.Kind == EdgeDropKind.Refused ? TryFindResource("HL.Tip.BulbFactory.EdgeFullDrop") as string : null;
            e.Effects = drop.Kind switch
            {
                EdgeDropKind.Refused => DragDropEffects.None,
                EdgeDropKind.Reorder => DragDropEffects.Move,
                _ => EffectFor(data, e),
            };
            ShowEdgeDrop(box, drop);
        }
        else if (HasFiles(e))
        {
            e.Effects = DragDropEffects.Copy;
            ShowEdgeDrop(box, new EdgeDrop(EdgeDropKind.Insert, InsertionIndex(box, e)));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnEdgeDrop(EdgeBox box, DragEventArgs e)
    {
        e.Handled = true;
        if (BulbDrag.From(e.Data) is { } data)
        {
            EdgeDrop drop = PlanEdgeDrop(box, data, e);
            data.DroppedOnFrame = true;
            EndDrop();
            bool move = EffectFor(data, e) == DragDropEffects.Move;
            ArrangementEdit? edit = (drop.Kind, data.Source) switch
            {
                (EdgeDropKind.Refused, _) => null,
                (EdgeDropKind.Reorder, { } source) => ArrangementEdits.Transfer(Arrangement, source, BoxPosition.OnEdge(box.Side, drop.Index), replace: false, move: true, NameOf),
                (EdgeDropKind.Replace, { } source) => ArrangementEdits.Transfer(Arrangement, source, BoxPosition.OnEdge(box.Side, drop.Index), replace: true, move, NameOf),
                (EdgeDropKind.Replace, null) => ArrangementEdits.Replace(Arrangement, box.Side, drop.Index, data.BulbIds[0], NameOf),
                (_, { } source) => ArrangementEdits.Transfer(Arrangement, source, BoxPosition.OnEdge(box.Side, drop.Index), replace: false, move, NameOf),
                _ => ArrangementEdits.Insert(Arrangement, box.Side, drop.Index, data.BulbIds, NameOf),
            };
            if (edit is not null)
            {
                Raise(edit);
                Settle(ArrangementTarget.ForChip(box.Side, Math.Min(drop.Index, edit.Result.GetEdge(box.Side).Count - 1)));
            }
        }
        else if (HasFiles(e))
        {
            EndDrop();
            FilesDropped?.Invoke(this, new FilesDroppedRequest(FilesOf(e), BoxPosition.OnEdge(box.Side, InsertionIndex(box, e))));
        }
    }

    private void OnCornerDragOver(CornerBox box, DragEventArgs e)
    {
        e.Handled = true;
        ClearDragMarks();
        if (BulbDrag.From(e.Data) is { } data)
        {
            data.OverFrame = true;
            data.Hint = null;
            bool same = data.Source is { Side: null } source && source.Corner == box.Corner;
            e.Effects = same ? DragDropEffects.None : EffectFor(data, e);
            box.ShowDragState(dropTargetsShown, over: !same, refused: false);
        }
        else if (HasFiles(e))
        {
            e.Effects = DragDropEffects.Copy;
            box.ShowDragState(dropTargetsShown, over: true, refused: false);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        Stage.HighlightedSlot = box.Slot;
    }

    private void OnCornerDrop(CornerBox box, DragEventArgs e)
    {
        e.Handled = true;
        if (BulbDrag.From(e.Data) is { } data)
        {
            data.DroppedOnFrame = true;
            EndDrop();
            BoxPosition destination = BoxPosition.InCorner(box.Corner);
            if (data.Source is { } source)
            {
                if (source != destination)
                {
                    Raise(ArrangementEdits.Transfer(Arrangement, source, destination, replace: true, EffectFor(data, e) == DragDropEffects.Move, NameOf));
                }
            }
            else
            {
                Raise(ArrangementEdits.SetCorner(Arrangement, box.Corner, data.BulbIds[0], NameOf));
            }

            Settle(ArrangementTarget.ForCorner(box.Corner));
        }
        else if (HasFiles(e))
        {
            EndDrop();
            FilesDropped?.Invoke(this, new FilesDroppedRequest(FilesOf(e), BoxPosition.InCorner(box.Corner)));
        }
    }

    /// <summary>Where a drop on the stage goes (PRODUCT-SPEC 3.2.3): a corner zone replaces the corner, elsewhere the nearest edge appends.</summary>
    private BoxPosition? StageDestination(DragEventArgs e)
    {
        if (Stage.GetDropTarget(e.GetPosition(Stage)) is not { } hit)
        {
            return null;
        }

        return hit.Corner is { } corner ? BoxPosition.InCorner(corner) : BoxPosition.OnEdge(hit.Side!.Value, Arrangement.GetEdge(hit.Side.Value).Count);
    }

    private void OnStageDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDragMarks();
        BulbDragData? data = BulbDrag.From(e.Data);
        if (data is not null)
        {
            data.OverFrame = true;
            data.Hint = null;
        }

        if ((data is null && !HasFiles(e)) || StageDestination(e) is not { } destination)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        bool full = destination.Side is { } side && Arrangement.GetEdge(side).Count >= SlotAssignment.MaxTypesPerEdge;
        BoxBase box = destination.Side is { } edge ? edges[edge] : corners[destination.Corner];
        if (full)
        {
            e.Effects = DragDropEffects.None;
            if (data is not null)
            {
                data.Hint = TryFindResource("HL.Tip.BulbFactory.EdgeFull") as string;
            }
        }
        else
        {
            e.Effects = data is null ? DragDropEffects.Copy : EffectFor(data, e);
        }

        box.ShowDragState(dropTargetsShown, over: true, refused: full);
        Stage.HighlightedSlot = box.Slot;
    }

    private void OnStageDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        BoxPosition? destination = StageDestination(e);
        BulbDragData? data = BulbDrag.From(e.Data);
        if (data is not null)
        {
            data.DroppedOnFrame = true;
        }

        EndDrop();
        if (destination is null)
        {
            return;
        }

        if (data is null)
        {
            if (HasFiles(e))
            {
                FilesDropped?.Invoke(this, new FilesDroppedRequest(FilesOf(e), destination));
            }

            return;
        }

        bool full = destination.Side is { } side && Arrangement.GetEdge(side).Count >= SlotAssignment.MaxTypesPerEdge;
        if (full || (data.Source is { } same && same == destination))
        {
            return;
        }

        ArrangementEdit edit = data.Source is { } source
            ? ArrangementEdits.Transfer(Arrangement, source, destination, replace: destination.Side is null, EffectFor(data, e) == DragDropEffects.Move, NameOf)
            : destination.Side is { } edge
                ? ArrangementEdits.AddTo(Arrangement, edge, data.BulbIds, NameOf)
                : ArrangementEdits.SetCorner(Arrangement, destination.Corner, data.BulbIds[0], NameOf);
        Raise(edit);
        Settle(destination.Side is { } settled
            ? ArrangementTarget.ForChip(settled, Math.Max(0, edit.Result.GetEdge(settled).Count - 1))
            : ArrangementTarget.ForCorner(destination.Corner));
    }

    private void EndDrop()
    {
        if (filesShowTargets)
        {
            filesShowTargets = false;
            dropTargetsShown = false;
        }

        ClearDragMarks();
    }

    /// <summary>Remembers where a drag of a chip's bulb may start (not on the chip's remove button).</summary>
    private void OnChipMouseDown(ArrangementChip chip, MouseButtonEventArgs e)
    {
        bool onButton = ElementTree.SelfAndAncestors(e.OriginalSource as DependencyObject)
            .TakeWhile(node => !ReferenceEquals(node, chip))
            .Any(node => node is System.Windows.Controls.Primitives.ButtonBase);

        dragStart = chip.BulbId is null || chip.IsPlus || onButton ? null : e.GetPosition(this);
    }

    /// <summary>Starts dragging a chip or a corner after the system drag distance; a drop outside the frame removes it with a 150 ms fade.</summary>
    private void OnChipMouseMove(ArrangementChip chip, MouseEventArgs e)
    {
        if (dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || services is null)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                dragStart = null;
            }

            return;
        }

        if (!BulbDrag.IsDragDistance(start, e.GetPosition(this)) || chip.BulbId is not { } bulbId || TargetOf(chip) is not { } target)
        {
            return;
        }

        dragStart = null;
        BoxPosition source = target.Kind == ArrangementTargetKind.Corner ? BoxPosition.InCorner(target.Corner) : BoxPosition.OnEdge(target.Side, target.Index);
        string removal = "Remove from " + (source.Side is { } side ? ArrangementTexts.EdgeTitle(side) : ArrangementTexts.CornerTitle(source.Corner));
        var data = new BulbDragData([bulbId], source);
        ShowDropTargets(true);
        bool droppedOutside;
        try
        {
            droppedOutside = BulbDrag.Run(chip, data, services, removal);
        }
        finally
        {
            ShowDropTargets(false);
        }

        if (droppedOutside)
        {
            RemoveWithFade(chip, target);
        }
    }

    private void RemoveWithFade(ArrangementChip chip, ArrangementTarget target)
    {
        ArrangementEdit edit = target.Kind == ArrangementTargetKind.Corner
            ? ArrangementEdits.ClearCorner(Arrangement, target.Corner, NameOf)
            : ArrangementEdits.Remove(Arrangement, target.Side, target.Index, NameOf);
        if (!SystemParameters.ClientAreaAnimation)
        {
            Raise(edit);
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            chip.BeginAnimation(OpacityProperty, null);
            Raise(edit);
        };
        chip.BeginAnimation(OpacityProperty, fade);
    }

    private enum EdgeDropKind
    {
        Insert,
        Reorder,
        Replace,
        Refused,
    }

    private readonly record struct EdgeDrop(EdgeDropKind Kind, int Index);
}
