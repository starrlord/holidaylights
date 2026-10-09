using System.Buffers.Binary;
using System.Text;
using HolidayLights.Core.Legacy;

namespace HolidayLights.Tests.Settings.Fakes;

/// <summary>Builds raw 5.4 registry values the way 5.4 wrote them.</summary>
internal static class LegacyRaw
{
    /// <summary>"Bulb Settings": 32 int32 ids (-1 = empty).</summary>
    public static LegacyValue BulbSettings(params int[] ids)
    {
        byte[] bytes = new byte[128];
        for (int i = 0; i < 32; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), i < ids.Length ? ids[i] : -1);
        }

        return LegacyValue.OfBinary(bytes);
    }

    /// <summary>The 32 ids of an arrangement given as edges and corners (5.4 record layout).</summary>
    public static int[] Ids(int[] top, int[] right, int[] bottom, int[] left, int topLeft = -1, int topRight = -1, int bottomLeft = -1, int bottomRight = -1)
    {
        int[] ids = [.. Enumerable.Repeat(-1, 32)];
        top.CopyTo(ids, 0);
        right.CopyTo(ids, 8);
        bottom.CopyTo(ids, 16);
        left.CopyTo(ids, 24);
        (ids[6], ids[7], ids[22], ids[23]) = (topLeft, topRight, bottomLeft, bottomRight);
        return ids;
    }

    /// <summary>A song list: names, each NUL-terminated, plus a final NUL.</summary>
    public static LegacyValue SongList(params string[] names) =>
        LegacyValue.OfBinary([.. names.SelectMany(n => Encoding.Latin1.GetBytes(n + "\0")), 0]);

    /// <summary>A LOGFONTA.</summary>
    public static LegacyValue LogFont(string face, int height, int weight, bool italic = false, bool underline = false, bool strikeout = false)
    {
        byte[] bytes = new byte[60];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, height);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), weight);
        (bytes[20], bytes[21], bytes[22]) = ((byte)(italic ? 1 : 0), (byte)(underline ? 1 : 0), (byte)(strikeout ? 1 : 0));
        Encoding.Latin1.GetBytes(face).CopyTo(bytes, 28);
        return LegacyValue.OfBinary(bytes);
    }
}
