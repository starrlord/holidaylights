namespace HolidayLights.Audio.Midi;

/// <summary>Where the sequencer sends MIDI: a WinMM device in the app, a recorder in tests. Used by one thread at a time.</summary>
internal interface IMidiOutput : IDisposable
{
    /// <summary>Sends a channel message.</summary>
    /// <param name="status">The status byte.</param>
    /// <param name="data1">The first data byte.</param>
    /// <param name="data2">The second data byte (0 for one-byte messages).</param>
    /// <exception cref="MidiOutputException">The device failed.</exception>
    void SendShort(byte status, byte data1, byte data2);

    /// <summary>Sends a SysEx message (or the raw bytes of an escaped 0xF7 event).</summary>
    /// <param name="message">The bytes.</param>
    /// <exception cref="MidiOutputException">The device failed.</exception>
    void SendSysEx(ReadOnlySpan<byte> message);
}
