using System.Diagnostics;
using System.Runtime.InteropServices;
using HolidayLights.Audio.Native;

namespace HolidayLights.Audio.Midi;

/// <summary>
/// A WinMM MIDI output handle (<c>midiOutShortMsg</c>, <c>midiOutLongMsg</c>). The Microsoft GS Wavetable Synth accepts
/// one handle at a time, so a handle is opened per song and closed when the song ends (as 5.4 opened MCI per song).
/// </summary>
internal sealed unsafe class WinMmMidiOutput : IMidiOutput
{
    /// <summary>How long a SysEx buffer may stay queued in the driver.</summary>
    private const int SysExTimeoutMilliseconds = 1000;

    private nint handle;

    private WinMmMidiOutput(nint handle) => this.handle = handle;

    /// <summary>Opens a device.</summary>
    /// <param name="deviceId">A device id or <see cref="NativeMethods.MidiMapper"/>.</param>
    /// <returns>The open output.</returns>
    /// <exception cref="MidiOutputException">The device cannot be opened (<see cref="MidiOutputException.IsDeviceBusy"/> when another app holds it).</exception>
    public static WinMmMidiOutput Open(uint deviceId)
    {
        nint opened;
        uint result = NativeMethods.midiOutOpen(&opened, deviceId, 0, 0, 0);
        if (result != NativeMethods.NoError)
        {
            string device = deviceId == NativeMethods.MidiMapper ? "the MIDI mapper" : $"MIDI output device {deviceId}";
            throw new MidiOutputException((int)result, result == NativeMethods.ErrorAllocated
                ? $"Another app is using {device} (MMRESULT {result})."
                : $"Couldn't open {device} (MMRESULT {result}).");
        }

        return new WinMmMidiOutput(opened);
    }

    /// <inheritdoc />
    public void SendShort(byte status, byte data1, byte data2)
    {
        uint result = NativeMethods.midiOutShortMsg(Handle, (uint)(status | (data1 << 8) | (data2 << 16)));
        if (result != NativeMethods.NoError)
        {
            throw new MidiOutputException((int)result, $"Sending a MIDI message failed (MMRESULT {result}).");
        }
    }

    /// <inheritdoc />
    public void SendSysEx(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty)
        {
            return;
        }

        nint device = Handle;
        var header = (NativeMethods.MidiHeader*)NativeMemory.AllocZeroed((nuint)sizeof(NativeMethods.MidiHeader));
        var buffer = (byte*)NativeMemory.Alloc((nuint)message.Length);
        try
        {
            message.CopyTo(new Span<byte>(buffer, message.Length));
            header->Data = buffer;
            header->BufferLength = header->BytesRecorded = (uint)message.Length;
            uint size = (uint)sizeof(NativeMethods.MidiHeader);
            Check(NativeMethods.midiOutPrepareHeader(device, header, size), "Preparing a SysEx message");
            try
            {
                Check(NativeMethods.midiOutLongMsg(device, header, size), "Sending a SysEx message");
                WaitUntilDone(header);
            }
            finally
            {
                var watch = Stopwatch.StartNew();
                while (NativeMethods.midiOutUnprepareHeader(device, header, size) == NativeMethods.ErrorStillPlaying
                       && watch.ElapsedMilliseconds < SysExTimeoutMilliseconds)
                {
                    Thread.Sleep(1);
                }
            }
        }
        finally
        {
            NativeMemory.Free(buffer);
            NativeMemory.Free(header);
        }
    }

    /// <summary>Silences the device (<c>midiOutReset</c>) and closes the handle.</summary>
    public void Dispose()
    {
        nint device = Interlocked.Exchange(ref handle, 0);
        if (device != 0)
        {
            NativeMethods.midiOutReset(device);
            NativeMethods.midiOutClose(device);
        }
    }

    private nint Handle => handle != 0 ? handle : throw new ObjectDisposedException(nameof(WinMmMidiOutput));

    private static void WaitUntilDone(NativeMethods.MidiHeader* header)
    {
        var watch = Stopwatch.StartNew();
        while ((Volatile.Read(ref header->Flags) & NativeMethods.HeaderDone) == 0 && watch.ElapsedMilliseconds < SysExTimeoutMilliseconds)
        {
            Thread.Sleep(1);
        }
    }

    private static void Check(uint result, string action)
    {
        if (result != NativeMethods.NoError)
        {
            throw new MidiOutputException((int)result, $"{action} failed (MMRESULT {result}).");
        }
    }
}
