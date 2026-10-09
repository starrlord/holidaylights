namespace HolidayLights.Audio.Midi;

/// <summary>A WinMM MIDI output error (<c>midiOutOpen</c>, <c>midiOutShortMsg</c>, <c>midiOutLongMsg</c>).</summary>
public sealed class MidiOutputException : IOException
{
    /// <summary><c>MMSYSERR_ALLOCATED</c>.</summary>
    private const int ErrorAllocated = 4;

    /// <summary>Creates the exception.</summary>
    /// <param name="errorCode">The <c>MMRESULT</c>.</param>
    /// <param name="message">What failed.</param>
    public MidiOutputException(int errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>The <c>MMRESULT</c> of the failed call.</summary>
    public int ErrorCode { get; }

    /// <summary>
    /// True for <c>MMSYSERR_ALLOCATED</c>: another app holds the synthesizer (the Microsoft GS Wavetable Synth accepts one
    /// handle at a time). The Music Box waits and tries again (PRODUCT-SPEC 3.4.6).
    /// </summary>
    public bool IsDeviceBusy => ErrorCode == ErrorAllocated;
}
