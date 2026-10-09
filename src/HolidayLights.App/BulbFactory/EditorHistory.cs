using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// Bulb Editing's own Undo and Redo (Ctrl+Z, Ctrl+Y; PRODUCT-SPEC 3.3.1): snapshots of the document's animations taken
/// before "Change...", "Remove Flavor" and "Copy to All Sides". The texts have their text boxes' own undo.
/// </summary>
internal sealed class EditorHistory
{
    private const int MaxSteps = 50;

    private readonly LinkedList<BulbDocument> undo = new();
    private readonly Stack<BulbDocument> redo = new();

    /// <summary>True when there is a step to undo.</summary>
    public bool CanUndo => undo.Count > 0;

    /// <summary>True when there is a step to redo.</summary>
    public bool CanRedo => redo.Count > 0;

    /// <summary>Remembers the document as it is before a change (the oldest of more than 50 steps is forgotten).</summary>
    /// <param name="before">The document before the change.</param>
    public void Record(BulbDocument before)
    {
        undo.AddLast(before.Clone());
        if (undo.Count > MaxSteps)
        {
            undo.RemoveFirst();
        }

        redo.Clear();
    }

    /// <summary>Goes back one step.</summary>
    /// <param name="current">The document now.</param>
    /// <returns>The document before the last change.</returns>
    /// <exception cref="InvalidOperationException">There is nothing to undo.</exception>
    public BulbDocument Undo(BulbDocument current)
    {
        BulbDocument previous = undo.Last?.Value ?? throw new InvalidOperationException("There is nothing to undo.");
        undo.RemoveLast();
        redo.Push(current.Clone());
        return previous;
    }

    /// <summary>Applies the last undone step again.</summary>
    /// <param name="current">The document now.</param>
    /// <returns>The document after the undone change.</returns>
    /// <exception cref="InvalidOperationException">There is nothing to redo.</exception>
    public BulbDocument Redo(BulbDocument current)
    {
        BulbDocument next = redo.Count > 0 ? redo.Pop() : throw new InvalidOperationException("There is nothing to redo.");
        undo.AddLast(current.Clone());
        return next;
    }
}
