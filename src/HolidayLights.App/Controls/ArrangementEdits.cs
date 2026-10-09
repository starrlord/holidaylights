namespace HolidayLights.App.Controls;

/// <summary>One change of the arrangement with its texts (PRODUCT-SPEC 3.2.4-3.2.12, Appendix D).</summary>
/// <param name="Result">The new arrangement (equal to the old one when nothing changes).</param>
/// <param name="UndoText">The Undo text ("Use Candy Canes for the whole frame").</param>
/// <param name="Announcement">The screen-reader sentence ("Candy Canes now on the whole frame.").</param>
/// <param name="Snackbar">The snackbar sentence for the big changes, or null.</param>
/// <param name="NextTarget">The target afterwards (a "+" that received a bulb moves to the new chip), or null to keep it.</param>
/// <param name="Problem">An InfoBar sentence when part of the change could not be made ("The bottom edge is full, ..."), or null.</param>
public sealed record ArrangementEdit(
    SlotAssignment Result,
    string UndoText,
    string Announcement,
    string? Snackbar = null,
    ArrangementTarget? NextTarget = null,
    string? Problem = null);

/// <summary>A box of the frame editor: an edge position (a chip or the insertion point) or a corner.</summary>
/// <param name="Side">The edge, or null for a corner.</param>
/// <param name="Index">The position in the edge.</param>
/// <param name="Corner">The corner, when <paramref name="Side"/> is null.</param>
public sealed record BoxPosition(Side? Side, int Index, Corner Corner)
{
    /// <summary>An edge position.</summary>
    /// <param name="side">The edge.</param>
    /// <param name="index">The position.</param>
    /// <returns>The box position.</returns>
    public static BoxPosition OnEdge(Side side, int index) => new(side, index, default);

    /// <summary>A corner.</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The box position.</returns>
    public static BoxPosition InCorner(Corner corner) => new(null, 0, corner);

    /// <summary>"the top edge" or "the top-left corner".</summary>
    public string Name => Side is { } side ? "the " + ArrangementTexts.EdgeName(side) : "the " + ArrangementTexts.CornerName(Corner);
}

/// <summary>
/// The rules of the frame editor as pure functions (PRODUCT-SPEC 3.2.2-3.2.7): using a bulb for a target, Add To, the
/// drag-and-drop rules (insert, replace on a full edge, copy or move between boxes, reorder, remove), clearing. Up to six
/// bulb types per edge; one bulb per corner; a bulb may appear anywhere any number of times.
/// </summary>
public static class ArrangementEdits
{
    private const int Max = SlotAssignment.MaxTypesPerEdge;

    /// <summary>Uses a bulb for the target (double-click, Enter, "Use for ...").</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="target">The target.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit Use(SlotAssignment arrangement, ArrangementTarget target, string bulbId, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(bulbId);
        ArgumentNullException.ThrowIfNull(nameOf);
        string name = nameOf(bulbId);
        switch (target.Kind)
        {
            case ArrangementTargetKind.WholeFrame:
                return new(AllEdgesAre(arrangement, bulbId).WithCorners(bulbId),
                    $"Use {name} for the whole frame", $"{name} now on the whole frame.", $"Using {name} on the whole frame.");
            case ArrangementTargetKind.AllEdges:
                return new(AllEdgesAre(arrangement, bulbId),
                    $"Use {name} for all four edges", $"{name} now on all four edges.", $"Using {name} on all four edges.");
            case ArrangementTargetKind.AllCorners:
                return new(arrangement.WithCorners(bulbId),
                    $"Use {name} in all four corners", $"{name} now in all four corners.", $"Using {name} in all four corners.");
            case ArrangementTargetKind.Corner:
                return new(arrangement.WithCorner(target.Corner, bulbId),
                    $"Use {name} for the {ArrangementTexts.CornerName(target.Corner)}", $"{name} now in the {ArrangementTexts.CornerName(target.Corner)}.");
            case ArrangementTargetKind.Chip when target.Index < arrangement.GetEdge(target.Side).Count:
                return Replace(arrangement, target.Side, target.Index, bulbId, nameOf);
            default:
                ArrangementEdit added = AddTo(arrangement, target.Side, [bulbId], nameOf);
                int count = added.Result.GetEdge(target.Side).Count;
                return added with { NextTarget = count > arrangement.GetEdge(target.Side).Count ? ArrangementTarget.ForChip(target.Side, count - 1) : null };
        }
    }

    /// <summary>"Add to ... Edge": appends the bulbs (in order) until the edge holds six types.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <param name="bulbIds">The bulbs.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit (with a problem sentence when the edge was full).</returns>
    public static ArrangementEdit AddTo(SlotAssignment arrangement, Side side, IReadOnlyList<string> bulbIds, Func<string, string> nameOf) =>
        Insert(arrangement, side, arrangement.GetEdge(side).Count, bulbIds, nameOf);

    /// <summary>"Add to Every Edge": appends to every edge that has room.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="bulbIds">The bulbs.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit; its problem names the edges that were full.</returns>
    public static ArrangementEdit AddToEveryEdge(SlotAssignment arrangement, IReadOnlyList<string> bulbIds, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(bulbIds);
        SlotAssignment result = arrangement;
        var full = new List<string>();
        foreach (Side side in CellSlots.Sides)
        {
            IReadOnlyList<string> edge = result.GetEdge(side);
            if (edge.Count + bulbIds.Count > Max)
            {
                full.Add(ArrangementTexts.SideWord(side));
            }

            result = result.WithEdge(side, edge.Concat(bulbIds).Take(Math.Max(edge.Count, Max)));
        }

        string what = Describe(bulbIds, nameOf);
        string? problem = full.Count == 0
            ? null
            : $"The {ArrangementTexts.JoinWithAnd(full)} {(full.Count == 1 ? "edge is" : "edges are")} full, so the {(bulbIds.Count == 1 ? "bulb was" : "bulbs were")} not added there.";
        return new(result, $"Add {what} to every edge", $"Added {what} to every edge.", Problem: problem);
    }

    /// <summary>Drop from the Bulb List between chips (or append): inserts up to six types, then stops.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <param name="index">The insertion position.</param>
    /// <param name="bulbIds">The bulbs.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit (unchanged with a problem when the edge is full).</returns>
    public static ArrangementEdit Insert(SlotAssignment arrangement, Side side, int index, IReadOnlyList<string> bulbIds, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(bulbIds);
        ArgumentNullException.ThrowIfNull(nameOf);
        IReadOnlyList<string> edge = arrangement.GetEdge(side);
        int room = Max - edge.Count;
        string edgeName = ArrangementTexts.EdgeName(side);
        if (room <= 0 || bulbIds.Count == 0)
        {
            return new(arrangement, $"Add a bulb to the {edgeName}", $"The {edgeName} already has 6 bulb types.",
                Problem: $"This edge already has {Max} bulb types. Remove one first.");
        }

        string[] added = [.. bulbIds.Take(room)];
        var list = edge.ToList();
        list.InsertRange(Math.Clamp(index, 0, list.Count), added);
        string what = Describe(added, nameOf);
        string? problem = added.Length < bulbIds.Count ? $"This edge already has {Max} bulb types. Remove one first." : null;
        return new(arrangement.WithEdge(side, list), $"Add {what} to the {edgeName}", $"Added {what} to the {edgeName}.", Problem: problem);
    }

    /// <summary>Replaces a chip (a drop on a chip of a full edge; using a bulb for a chip).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <param name="index">The chip.</param>
    /// <param name="bulbId">The new bulb.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit Replace(SlotAssignment arrangement, Side side, int index, string bulbId, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(nameOf);
        IReadOnlyList<string> edge = arrangement.GetEdge(side);
        if (index < 0 || index >= edge.Count)
        {
            return AddTo(arrangement, side, [bulbId], nameOf);
        }

        string oldName = nameOf(edge[index]);
        string name = nameOf(bulbId);
        string[] list = [.. edge];
        list[index] = bulbId;
        string edgeName = ArrangementTexts.EdgeName(side);
        return new(arrangement.WithEdge(side, list), $"Replace {oldName} with {name} on the {edgeName}", $"{oldName} replaced by {name} on the {edgeName}.");
    }

    /// <summary>Puts a bulb in a corner (Add To a corner, a drop on a corner).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="corner">The corner.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit SetCorner(SlotAssignment arrangement, Corner corner, string bulbId, Func<string, string> nameOf) =>
        Use(arrangement, ArrangementTarget.ForCorner(corner), bulbId, nameOf);

    /// <summary>Removes a chip (Delete, the chip's remove button, a drop outside the frame).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <param name="index">The chip.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit Remove(SlotAssignment arrangement, Side side, int index, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(nameOf);
        IReadOnlyList<string> edge = arrangement.GetEdge(side);
        if (index < 0 || index >= edge.Count)
        {
            return new(arrangement, "Remove a bulb", "Nothing to remove.");
        }

        string name = nameOf(edge[index]);
        string edgeName = ArrangementTexts.EdgeName(side);
        return new(arrangement.WithEdge(side, edge.Where((_, i) => i != index)), $"Remove {name} from the {edgeName}", $"Removed {name} from the {edgeName}.",
            NextTarget: edge.Count > 1 ? ArrangementTarget.ForChip(side, Math.Min(index, edge.Count - 2)) : ArrangementTarget.ForPlus(side));
    }

    /// <summary>Empties a corner.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="corner">The corner.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit ClearCorner(SlotAssignment arrangement, Corner corner, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(nameOf);
        string cornerName = ArrangementTexts.CornerName(corner);
        return arrangement.GetCorner(corner) is { } id
            ? new(arrangement.WithCorner(corner, null), $"Remove {nameOf(id)} from the {cornerName}", $"Removed {nameOf(id)} from the {cornerName}.")
            : new(arrangement, $"Clear the {cornerName}", $"The {cornerName} is empty.");
    }

    /// <summary>Moves a chip within its edge (drag inside a box, Ctrl+arrows).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <param name="from">The chip.</param>
    /// <param name="to">Its new position.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit; the target follows the chip.</returns>
    public static ArrangementEdit Reorder(SlotAssignment arrangement, Side side, int from, int to, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(nameOf);
        var list = arrangement.GetEdge(side).ToList();
        if (from < 0 || from >= list.Count)
        {
            return new(arrangement, "Move a bulb", "Nothing to move.");
        }

        to = Math.Clamp(to, 0, list.Count - 1);
        string id = list[from];
        list.RemoveAt(from);
        list.Insert(to, id);
        string edgeName = ArrangementTexts.EdgeName(side);
        return new(arrangement.WithEdge(side, list), $"Move {nameOf(id)} on the {edgeName}",
            $"Moved {nameOf(id)} to position {to + 1} of {list.Count} on the {edgeName}.", NextTarget: ArrangementTarget.ForChip(side, to));
    }

    /// <summary>
    /// Drags a chip or a corner to another box (PRODUCT-SPEC 3.2.7): copies, or moves with Shift. On an edge the bulb is
    /// inserted at the drop position (a full edge only accepts a drop on a chip, which is replaced); a corner is replaced.
    /// </summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="source">Where the bulb was dragged from.</param>
    /// <param name="destination">Where it was dropped.</param>
    /// <param name="replace">True when dropped on a chip of a full edge.</param>
    /// <param name="move">True to move instead of copy.</param>
    /// <param name="nameOf">Bulb names by id.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit Transfer(SlotAssignment arrangement, BoxPosition source, BoxPosition destination, bool replace, bool move, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(nameOf);
        string? id = source.Side is { } fromSide
            ? (source.Index < arrangement.GetEdge(fromSide).Count ? arrangement.GetEdge(fromSide)[source.Index] : null)
            : arrangement.GetCorner(source.Corner);
        if (id is null)
        {
            return new(arrangement, "Move a bulb", "Nothing to move.");
        }

        if (source.Side is { } sameSide && destination.Side == sameSide && !replace)
        {
            int to = destination.Index > source.Index ? destination.Index - 1 : destination.Index;
            return Reorder(arrangement, sameSide, source.Index, to, nameOf);
        }

        SlotAssignment result = arrangement;
        if (move)
        {
            result = source.Side is { } side ? result.WithEdge(side, result.GetEdge(side).Where((_, i) => i != source.Index)) : result.WithCorner(source.Corner, null);
        }

        int index = destination.Index;
        if (move && source.Side is { } removedFrom && destination.Side == removedFrom && source.Index < index)
        {
            index--;
        }

        ArrangementEdit placed;
        if (destination.Side is { } toSide)
        {
            placed = replace ? Replace(result, toSide, index, id, nameOf) : Insert(result, toSide, index, [id], nameOf);
        }
        else
        {
            placed = SetCorner(result, destination.Corner, id, nameOf);
        }

        if (placed.Problem is not null && placed.Result.Equals(result))
        {
            return placed with { Result = arrangement };
        }

        string name = nameOf(id);
        string verb = move ? "Move" : "Copy";
        string past = move ? "Moved" : "Copied";
        return placed with
        {
            UndoText = $"{verb} {name} to {destination.Name}",
            Announcement = $"{past} {name} to {destination.Name}.",
            NextTarget = null,
        };
    }

    /// <summary>"Copy This Edge to All Edges": the other three edges get this edge's list.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit CopyEdgeToAll(SlotAssignment arrangement, Side side)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        IReadOnlyList<string> ids = arrangement.GetEdge(side);
        SlotAssignment result = CellSlots.Sides.Aggregate(arrangement, (a, s) => a.WithEdge(s, ids));
        string edgeName = ArrangementTexts.EdgeName(side);
        return new(result, $"Copy the {edgeName} to all edges", $"Every edge now has the bulbs of the {edgeName}.");
    }

    /// <summary>"Clear" on an edge box.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="side">The edge.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit ClearEdge(SlotAssignment arrangement, Side side)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        string edgeName = ArrangementTexts.EdgeName(side);
        return new(arrangement.WithEdge(side, []), $"Clear the {edgeName}", $"Cleared the {edgeName}.", NextTarget: ArrangementTarget.ForPlus(side));
    }

    /// <summary>"Clear All Bulbs": empties all 8 boxes (one undo step, a snackbar, no confirmation).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit ClearAll(SlotAssignment arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        return new(SlotAssignment.Empty, "Clear all bulbs", "Cleared all edges and corners.", "Cleared all edges and corners.", ArrangementTarget.WholeFrame);
    }

    /// <summary>"Remove Missing Bulbs": takes every bulb whose file is missing out of the boxes.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="exists">True when a bulb id resolves.</param>
    /// <returns>The edit.</returns>
    public static ArrangementEdit RemoveMissing(SlotAssignment arrangement, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(exists);
        SlotAssignment result = CellSlots.Sides.Aggregate(arrangement, (a, s) => a.WithEdge(s, a.GetEdge(s).Where(exists)));
        result = CellSlots.Corners.Aggregate(result, (a, c) => a.GetCorner(c) is { } id && !exists(id) ? a.WithCorner(c, null) : a);
        return new(result, "Remove missing bulbs", "Removed the missing bulbs from the edges and corners.");
    }

    /// <summary>Takes a bulb out of every box ("Remove Bulb" on a bulb in use).</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <returns>The new arrangement.</returns>
    public static SlotAssignment Without(SlotAssignment arrangement, string bulbId)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        SlotAssignment result = CellSlots.Sides.Aggregate(arrangement, (a, s) => a.WithEdge(s, a.GetEdge(s).Where(id => !BulbIds.Comparer.Equals(id, bulbId))));
        return CellSlots.Corners.Aggregate(result, (a, c) => BulbIds.Comparer.Equals(a.GetCorner(c), bulbId) ? a.WithCorner(c, null) : a);
    }

    /// <summary>Where a bulb is in the arrangement, for the selected-bulb bar ("On your screen: Top Edge, Bottom-Left").</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <returns>The boxes in Title Case, edges first.</returns>
    public static IReadOnlyList<string> PlacesOf(SlotAssignment arrangement, string bulbId)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        var places = CellSlots.Sides.Where(s => arrangement.GetEdge(s).Contains(bulbId, BulbIds.Comparer)).Select(ArrangementTexts.EdgeTitle).ToList();
        places.AddRange(CellSlots.Corners.Where(c => BulbIds.Comparer.Equals(arrangement.GetCorner(c), bulbId)).Select(ArrangementTexts.CornerCaption));
        return places;
    }

    private static SlotAssignment AllEdgesAre(SlotAssignment arrangement, string bulbId) =>
        CellSlots.Sides.Aggregate(arrangement, (a, s) => a.WithEdge(s, [bulbId]));

    private static SlotAssignment WithCorners(this SlotAssignment arrangement, string bulbId) =>
        CellSlots.Corners.Aggregate(arrangement, (a, c) => a.WithCorner(c, bulbId));

    private static string Describe(IReadOnlyList<string> bulbIds, Func<string, string> nameOf) =>
        bulbIds.Count == 1 ? nameOf(bulbIds[0]) : $"{bulbIds.Count} bulbs";
}
