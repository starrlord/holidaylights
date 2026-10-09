using System.Buffers.Binary;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The 5.4 "Bulb Settings" value: 4 records of 8 int32 bulb ids, -1 = empty.
/// Ints 0-5 top (left to right), 6/7 top-left/top-right, 8-13 right, 16-21 bottom, 22/23 bottom-left/bottom-right,
/// 24-29 left.
/// </summary>
internal static class LegacyBulbSettings
{
    /// <summary>The number of ids.</summary>
    public const int Count = 32;

    /// <summary>The 5.4 default (also the installer's "Christmas 1").</summary>
    public static IReadOnlyList<int> Default { get; } =
    [
        0, -1, -1, -1, -1, -1, 1, 1,
        0, 2, -1, -1, -1, -1, -1, -1,
        2, 1, -1, -1, -1, -1, 1, 1,
        0, 2, -1, -1, -1, -1, -1, -1,
    ];

    /// <summary>
    /// Decodes the value as 5.4 loads it: up to 32 bytes is the legacy format (one signed byte per id, the
    /// rest -1 as the reference decoder pads); 33-128 bytes overwrite the start of the default table; a missing, empty or
    /// longer value gives the default.
    /// </summary>
    /// <param name="data">The REG_BINARY value, or null when missing.</param>
    /// <returns>The 32 ids.</returns>
    public static int[] Decode(byte[]? data)
    {
        int[] ids = [.. Default];
        if (data is null || data.Length == 0 || data.Length > Count * 4)
        {
            return ids;
        }

        if (data.Length <= Count)
        {
            for (int i = 0; i < Count; i++)
            {
                ids[i] = i < data.Length ? (sbyte)data[i] : -1;
            }

            return ids;
        }

        byte[] buffer = new byte[Count * 4];
        for (int i = 0; i < Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(i * 4), ids[i]);
        }

        data.CopyTo(buffer, 0);
        for (int i = 0; i < Count; i++)
        {
            ids[i] = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i * 4));
        }

        return ids;
    }

    /// <summary>Converts the ids into an arrangement; ids that do not resolve are dropped (5.4 compacted unusable ids).</summary>
    /// <param name="ids">The 32 ids.</param>
    /// <param name="resolve">Maps a 5.4 id to a bulb id, or null when the bulb is not installed.</param>
    /// <param name="unresolved">Receives the ids that were dropped.</param>
    /// <returns>The arrangement.</returns>
    public static SlotAssignment ToArrangement(IReadOnlyList<int> ids, Func<int, string?> resolve, ICollection<int> unresolved)
    {
        IReadOnlyList<string> Edge(int record)
        {
            var edge = new List<string>();
            for (int i = record * 8; i < (record * 8) + SlotAssignment.MaxTypesPerEdge; i++)
            {
                if (ids[i] != -1 && Resolve(ids[i]) is { } id)
                {
                    edge.Add(id);
                }
            }

            return edge;
        }

        string? Resolve(int legacyId)
        {
            if (legacyId == -1)
            {
                return null;
            }

            string? id = resolve(legacyId);
            if (id is null)
            {
                unresolved.Add(legacyId);
            }

            return id;
        }

        return new SlotAssignment
        {
            Top = Edge(0),
            Right = Edge(1),
            Bottom = Edge(2),
            Left = Edge(3),
            TopLeft = Resolve(ids[6]),
            TopRight = Resolve(ids[7]),
            BottomLeft = Resolve(ids[22]),
            BottomRight = Resolve(ids[23]),
        };
    }
}
