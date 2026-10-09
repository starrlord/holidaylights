namespace HolidayLights.Core.Abstractions;

/// <summary>
/// The arrangement: which bulbs are in the 8 boxes (PRODUCT-SPEC 3.2.2). Each edge holds an ordered list of 0-6 bulb ids
/// (a bulb may repeat); each corner holds one id or nothing.
/// </summary>
/// <remarks>
/// <para>On screen, position <c>i</c> of an edge shows type <c>ids[i % n]</c> with flavor <c>i / n</c> (5.4). Ids that do
/// not resolve (missing files) are skipped by the layout, as 5.4 compacted unusable ids.</para>
/// <para>Persisted in <c>settings.json</c> (<c>current.arrangement</c>) and theme files with the JSON property names
/// <c>top, right, bottom, left, topLeft, topRight, bottomLeft, bottomRight</c>.</para>
/// <para>Equality compares the lists element by element with <see cref="BulbIds.Comparer"/>.</para>
/// <para>Like every persisted contract type its properties have setters (System.Text.Json source generation cannot keep
/// initializer defaults of <c>init</c> properties); treat instances as immutable and change them with <c>with</c> or the
/// <c>With...</c> methods.</para>
/// </remarks>
public sealed record SlotAssignment
{
    /// <summary>The most bulb types an edge can hold (6 on every edge; PO decision 4).</summary>
    public const int MaxTypesPerEdge = 6;

    /// <summary>No bulbs anywhere ("Blank Slate"). A new instance on every call.</summary>
    public static SlotAssignment Empty => new();

    /// <summary>
    /// The 5.4 default arrangement (registry default and theme "Christmas 1"): top Standard Bulbs; right and left Standard
    /// Bulbs + Snow Family; bottom Snow Family + Jolly Holly; Jolly Holly in every corner. A new instance on every call.
    /// </summary>
    public static SlotAssignment Classic54Default => new()
    {
        Top = [BulbIds.BuiltIn("standard-bulbs")],
        Right = [BulbIds.BuiltIn("standard-bulbs"), BulbIds.BuiltIn("snow-family")],
        Bottom = [BulbIds.BuiltIn("snow-family"), BulbIds.BuiltIn("jolly-holly")],
        Left = [BulbIds.BuiltIn("standard-bulbs"), BulbIds.BuiltIn("snow-family")],
        TopLeft = BulbIds.BuiltIn("jolly-holly"),
        TopRight = BulbIds.BuiltIn("jolly-holly"),
        BottomLeft = BulbIds.BuiltIn("jolly-holly"),
        BottomRight = BulbIds.BuiltIn("jolly-holly"),
    };

    /// <summary>Top edge, left to right.</summary>
    public IReadOnlyList<string> Top { get; set; } = [];

    /// <summary>Right edge, top to bottom.</summary>
    public IReadOnlyList<string> Right { get; set; } = [];

    /// <summary>Bottom edge, left to right.</summary>
    public IReadOnlyList<string> Bottom { get; set; } = [];

    /// <summary>Left edge, top to bottom.</summary>
    public IReadOnlyList<string> Left { get; set; } = [];

    /// <summary>Top-left corner (null = empty).</summary>
    public string? TopLeft { get; set; }

    /// <summary>Top-right corner (null = empty).</summary>
    public string? TopRight { get; set; }

    /// <summary>Bottom-left corner (null = empty).</summary>
    public string? BottomLeft { get; set; }

    /// <summary>Bottom-right corner (null = empty).</summary>
    public string? BottomRight { get; set; }

    /// <summary>Returns the ids of one edge.</summary>
    /// <param name="side">The edge.</param>
    /// <returns>0-6 ids in screen order.</returns>
    public IReadOnlyList<string> GetEdge(Side side) => side switch
    {
        Side.Top => Top,
        Side.Right => Right,
        Side.Bottom => Bottom,
        Side.Left => Left,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };

    /// <summary>Returns the id in one corner.</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The id, or null when the corner is empty.</returns>
    public string? GetCorner(Corner corner) => corner switch
    {
        Corner.TopLeft => TopLeft,
        Corner.TopRight => TopRight,
        Corner.BottomRight => BottomRight,
        Corner.BottomLeft => BottomLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };

    /// <summary>Returns a copy with one edge replaced.</summary>
    /// <param name="side">The edge.</param>
    /// <param name="ids">The new ids (at most <see cref="MaxTypesPerEdge"/>).</param>
    /// <returns>The new arrangement.</returns>
    /// <exception cref="ArgumentException">More than <see cref="MaxTypesPerEdge"/> ids, or an empty id.</exception>
    public SlotAssignment WithEdge(Side side, IEnumerable<string> ids)
    {
        string[] list = [.. ids];
        if (list.Length > MaxTypesPerEdge)
        {
            throw new ArgumentException($"An edge holds at most {MaxTypesPerEdge} bulb types.", nameof(ids));
        }

        if (list.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Bulb ids must not be empty.", nameof(ids));
        }

        return side switch
        {
            Side.Top => this with { Top = list },
            Side.Right => this with { Right = list },
            Side.Bottom => this with { Bottom = list },
            Side.Left => this with { Left = list },
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };
    }

    /// <summary>Returns a copy with one corner replaced.</summary>
    /// <param name="corner">The corner.</param>
    /// <param name="id">The new id, or null to empty the corner.</param>
    /// <returns>The new arrangement.</returns>
    public SlotAssignment WithCorner(Corner corner, string? id) => corner switch
    {
        Corner.TopLeft => this with { TopLeft = id },
        Corner.TopRight => this with { TopRight = id },
        Corner.BottomRight => this with { BottomRight = id },
        Corner.BottomLeft => this with { BottomLeft = id },
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };

    /// <summary>Every id in the arrangement (edges in Top, Right, Bottom, Left order, then corners clockwise), with repeats.</summary>
    /// <returns>The ids.</returns>
    public IEnumerable<string> EnumerateBulbIds()
    {
        foreach (Side side in CellSlots.Sides)
        {
            foreach (string id in GetEdge(side))
            {
                yield return id;
            }
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            if (GetCorner(corner) is { } id)
            {
                yield return id;
            }
        }
    }

    /// <summary>True when no box holds a bulb.</summary>
    /// <returns>True for an empty arrangement.</returns>
    public bool HasNoBulbs() => !EnumerateBulbIds().Any();

    /// <summary>True when any box holds the bulb.</summary>
    /// <param name="bulbId">The bulb id.</param>
    /// <returns>True when the bulb is in use.</returns>
    public bool Contains(string bulbId) => EnumerateBulbIds().Contains(bulbId, BulbIds.Comparer);

    /// <summary>Compares two arrangements box by box (ids compared with <see cref="BulbIds.Comparer"/>).</summary>
    /// <param name="other">The other arrangement.</param>
    /// <returns>True when every box holds the same ids in the same order.</returns>
    public bool Equals(SlotAssignment? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && Top.SequenceEqual(other.Top, BulbIds.Comparer)
            && Right.SequenceEqual(other.Right, BulbIds.Comparer)
            && Bottom.SequenceEqual(other.Bottom, BulbIds.Comparer)
            && Left.SequenceEqual(other.Left, BulbIds.Comparer)
            && BulbIds.Comparer.Equals(TopLeft, other.TopLeft)
            && BulbIds.Comparer.Equals(TopRight, other.TopRight)
            && BulbIds.Comparer.Equals(BottomLeft, other.BottomLeft)
            && BulbIds.Comparer.Equals(BottomRight, other.BottomRight);
    }

    /// <summary>Hash code consistent with <see cref="Equals(SlotAssignment?)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (string id in EnumerateBulbIds())
        {
            hash.Add(id, BulbIds.Comparer);
        }

        return hash.ToHashCode();
    }
}
