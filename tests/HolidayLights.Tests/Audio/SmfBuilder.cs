using System.Buffers.Binary;

namespace HolidayLights.Tests.Audio;

/// <summary>Builds Standard MIDI File bytes for tests.</summary>
internal static class SmfBuilder
{
    /// <summary>End-of-Track with delta 0.</summary>
    public static readonly byte[] EndOfTrack = [0x00, 0xFF, 0x2F, 0x00];

    /// <summary>A complete file.</summary>
    public static byte[] Smf(int format, int division, params byte[][] tracks) =>
        [.. Header(format, tracks.Length, division), .. tracks.SelectMany(t => t)];

    /// <summary>An MThd chunk.</summary>
    public static byte[] Header(int format, int tracks, int division)
    {
        var header = new byte[14];
        "MThd"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 6);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)format);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)tracks);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12), (ushort)division);
        return header;
    }

    /// <summary>An MTrk chunk from bytes, numbers and byte arrays.</summary>
    public static byte[] Track(params object[] parts)
    {
        byte[] body = parts.SelectMany(p => p is byte[] bytes ? bytes : [Convert.ToByte(p)]).ToArray();
        var chunk = new byte[8 + body.Length];
        "MTrk"u8.CopyTo(chunk);
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(4), body.Length);
        body.CopyTo(chunk, 8);
        return chunk;
    }

    /// <summary>A little-endian 32-bit value.</summary>
    public static byte[] LittleEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
