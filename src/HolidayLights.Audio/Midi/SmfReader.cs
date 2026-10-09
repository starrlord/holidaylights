using System.Buffers.Binary;

namespace HolidayLights.Audio.Midi;

/// <summary>One event of a track exactly as stored in the file.</summary>
/// <param name="Tick">Absolute tick from the start of the track.</param>
/// <param name="Status">Channel status (0x80-0xEF), 0xF0/0xF7 (SysEx) or 0xFF (meta).</param>
/// <param name="Data1">First data byte of a channel message.</param>
/// <param name="Data2">Second data byte of a channel message (0 for one-byte messages).</param>
/// <param name="MetaType">Meta event type (0xFF events only).</param>
/// <param name="Payload">Meta data, or SysEx bytes (0xF0 events start with 0xF0, 0xF7 events are the raw escaped bytes).</param>
internal readonly record struct SmfEvent(long Tick, byte Status, byte Data1, byte Data2, byte MetaType, byte[]? Payload)
{
    /// <summary>The End-of-Track meta event type.</summary>
    public const byte EndOfTrackType = 0x2F;

    /// <summary>The Set Tempo meta event type.</summary>
    public const byte SetTempoType = 0x51;

    /// <summary>True for meta events.</summary>
    public bool IsMeta => Status == 0xFF;

    /// <summary>True for SysEx events (0xF0 and the 0xF7 escape).</summary>
    public bool IsSysEx => Status is 0xF0 or 0xF7;

    /// <summary>True for channel voice messages.</summary>
    public bool IsChannelMessage => Status is >= 0x80 and < 0xF0;

    /// <summary>True for End-of-Track.</summary>
    public bool IsEndOfTrack => IsMeta && MetaType == EndOfTrackType;
}

/// <summary>One MTrk chunk.</summary>
/// <param name="Length">Bytes of the chunk that were present in the file.</param>
/// <param name="Events">Events in file order, End-of-Track included.</param>
/// <param name="EndTick">Tick of the last event read (normally End-of-Track).</param>
/// <param name="HasEndOfTrack">False when the track ended without End-of-Track (truncated or damaged).</param>
internal sealed record SmfTrack(int Length, IReadOnlyList<SmfEvent> Events, long EndTick, bool HasEndOfTrack);

/// <summary>A Standard MIDI File split into its tracks.</summary>
/// <param name="Format">SMF format (0, 1 or 2).</param>
/// <param name="DeclaredTrackCount">The track count of the header.</param>
/// <param name="Division">The raw division (ticks per quarter note, or SMPTE when bit 15 is set).</param>
/// <param name="Tracks">The MTrk chunks read.</param>
internal sealed record SmfDocument(int Format, int DeclaredTrackCount, int Division, IReadOnlyList<SmfTrack> Tracks);

/// <summary>
/// Reads Standard MIDI Files as leniently as Windows' sequencer: an <c>.rmi</c> RIFF wrapper is unwrapped, unknown chunks
/// are skipped, running status survives meta and SysEx events, and a damaged or truncated track keeps the events read
/// before the damage. Only a file without an <c>MThd</c> header (or with division 0) is rejected.
/// </summary>
internal static class SmfReader
{
    private const int MaxVariableLengthBytes = 4;

    /// <summary>Reads the tracks of a file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidDataException">Not a Standard MIDI File.</exception>
    public static SmfDocument Read(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> smf = UnwrapRiff(data);
        if (smf.Length < 14 || !smf.StartsWith("MThd"u8))
        {
            throw new InvalidDataException("The file is not a Standard MIDI File.");
        }

        uint headerLength = BinaryPrimitives.ReadUInt32BigEndian(smf[4..]);
        if (headerLength < 6 || headerLength > smf.Length - 8)
        {
            throw new InvalidDataException("The MIDI file header is damaged.");
        }

        int format = BinaryPrimitives.ReadUInt16BigEndian(smf[8..]);
        int declaredTracks = BinaryPrimitives.ReadUInt16BigEndian(smf[10..]);
        int division = BinaryPrimitives.ReadUInt16BigEndian(smf[12..]);
        if (division == 0 || ((division & 0x8000) != 0 && (division & 0xFF) == 0))
        {
            throw new InvalidDataException("The MIDI file has no time division.");
        }

        var tracks = new List<SmfTrack>(declaredTracks);
        int position = 8 + (int)headerLength;
        while (position <= smf.Length - 8)
        {
            uint declaredLength = BinaryPrimitives.ReadUInt32BigEndian(smf[(position + 4)..]);
            int start = position + 8;
            int end = (int)Math.Min(smf.Length, start + (long)declaredLength);
            if (smf.Slice(position, 4).SequenceEqual("MTrk"u8))
            {
                tracks.Add(ReadTrack(smf[start..end]));
            }

            position = end;
        }

        return new SmfDocument(format, declaredTracks, division, tracks);
    }

    /// <summary>Returns the <c>data</c> chunk of a RIFF <c>RMID</c> file, or the input when it is not wrapped.</summary>
    private static ReadOnlySpan<byte> UnwrapRiff(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data.StartsWith("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("RMID"u8))
        {
            return data;
        }

        int position = 12;
        while (position <= data.Length - 8)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            int start = position + 8;
            int end = (int)Math.Min(data.Length, start + (long)length);
            if (data.Slice(position, 4).SequenceEqual("data"u8))
            {
                return data[start..end];
            }

            // RIFF chunks are padded to an even length.
            position = (int)Math.Min(data.Length, end + (long)(length & 1));
        }

        throw new InvalidDataException("The RIFF MIDI file has no data chunk.");
    }

    private static SmfTrack ReadTrack(ReadOnlySpan<byte> track)
    {
        var events = new List<SmfEvent>();
        int p = 0;
        long tick = 0;
        byte running = 0;
        bool endOfTrack = false;
        while (p < track.Length)
        {
            if (!TryReadVariableLength(track, ref p, out long delta))
            {
                break;
            }

            tick += delta;
            if (p >= track.Length)
            {
                break;
            }

            byte lead = track[p];
            if (lead == 0xFF)
            {
                if (p + 1 >= track.Length)
                {
                    break;
                }

                byte type = track[p + 1];
                p += 2;
                if (!TryReadPayload(track, ref p, out byte[] payload))
                {
                    break;
                }

                events.Add(new SmfEvent(tick, 0xFF, 0, 0, type, payload));
                if (type == SmfEvent.EndOfTrackType)
                {
                    endOfTrack = true;
                    break;
                }

                continue;
            }

            if (lead is 0xF0 or 0xF7)
            {
                p++;
                if (!TryReadPayload(track, ref p, out byte[] payload))
                {
                    break;
                }

                byte[] sysEx = lead == 0xF0 ? [0xF0, .. payload] : payload;
                events.Add(new SmfEvent(tick, lead, 0, 0, 0, sysEx));
                continue;
            }

            if (!TryReadChannelMessage(track, ref p, ref running, out byte status, out byte data1, out byte data2))
            {
                break;
            }

            events.Add(new SmfEvent(tick, status, data1, data2, 0, null));
        }

        return new SmfTrack(track.Length, events, tick, endOfTrack);
    }

    private static bool TryReadChannelMessage(
        ReadOnlySpan<byte> track, ref int p, ref byte running, out byte status, out byte data1, out byte data2)
    {
        status = data1 = data2 = 0;
        byte lead = track[p];
        if (lead >= 0x80)
        {
            if (lead >= 0xF0)
            {
                // System common and real-time messages are not allowed in a file: the rest of the track is unreadable.
                return false;
            }

            running = lead;
            p++;
        }
        else if (running == 0)
        {
            // A data byte without running status.
            return false;
        }

        status = running;
        int length = (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
        if (p + length > track.Length)
        {
            return false;
        }

        data1 = track[p];
        data2 = length == 2 ? track[p + 1] : (byte)0;
        if (((data1 | data2) & 0x80) != 0)
        {
            // A status byte inside the data: the track is damaged from here on.
            return false;
        }

        p += length;
        return true;
    }

    private static bool TryReadPayload(ReadOnlySpan<byte> track, ref int p, out byte[] payload)
    {
        payload = [];
        if (!TryReadVariableLength(track, ref p, out long length) || length > track.Length - p)
        {
            return false;
        }

        payload = track.Slice(p, (int)length).ToArray();
        p += (int)length;
        return true;
    }

    private static bool TryReadVariableLength(ReadOnlySpan<byte> track, ref int p, out long value)
    {
        value = 0;
        for (int i = 0; i < MaxVariableLengthBytes; i++)
        {
            if (p >= track.Length)
            {
                return false;
            }

            byte b = track[p++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
