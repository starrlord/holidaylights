using HolidayLights.Audio.Native;

namespace HolidayLights.Audio.Midi;

/// <summary>The WinMM MIDI output devices and the "MIDI Output" choice (PRODUCT-SPEC 3.4.5, 6.1.3). Thread-safe.</summary>
internal static unsafe class MidiOutputDevices
{
    /// <summary>The part of the name that identifies the Microsoft GS Wavetable Synth (device 0 on a stock Windows 11).</summary>
    private const string GsWavetableName = "GS Wavetable";

    /// <summary>
    /// The Microsoft GS Wavetable Synth's own latency: it renders into a buffer of its own before Windows mixes it. Measured
    /// on the reference PC (WASAPI loopback with QPC packet timestamps, 2026-10-08): its first audio reaches the audio engine
    /// 216-220 ms after <c>midiOutShortMsg</c>, 179 ms behind a shared-mode stream started at the same instant. Together
    /// with the default <c>music.syncOffsetMs</c> (40 ms: the audio engine, device buffers and speakers) a note is heard
    /// 230 ms after it is sent.
    /// </summary>
    public static readonly TimeSpan GsWavetableLatency = TimeSpan.FromMilliseconds(190);

    /// <summary>The names of the output devices in device order.</summary>
    /// <returns>Names; a device whose capabilities cannot be read is listed as "MIDI Output &lt;n&gt;".</returns>
    public static IReadOnlyList<string> GetNames()
    {
        uint count = NativeMethods.midiOutGetNumDevs();
        var names = new List<string>((int)count);
        for (uint id = 0; id < count; id++)
        {
            NativeMethods.MidiOutCaps caps;
            names.Add(NativeMethods.midiOutGetDevCapsW(id, &caps, (uint)sizeof(NativeMethods.MidiOutCaps)) == NativeMethods.NoError
                ? ReadName(caps)
                : $"MIDI Output {id + 1}");
        }

        return names;
    }

    /// <summary>
    /// The device to open: the configured name, else the first device whose name contains "GS Wavetable", else device 0;
    /// the MIDI mapper only when no device exists.
    /// </summary>
    /// <param name="configuredName">"MIDI Output" (<c>music.midiDevice</c>); empty = automatic.</param>
    /// <param name="names">The device names in device order.</param>
    /// <returns>The WinMM device id.</returns>
    public static uint Choose(string configuredName, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return NativeMethods.MidiMapper;
        }

        int chosen = string.IsNullOrWhiteSpace(configuredName)
            ? -1
            : FindIndex(names, n => string.Equals(n, configuredName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosen < 0)
        {
            chosen = FindIndex(names, n => n.Contains(GsWavetableName, StringComparison.OrdinalIgnoreCase));
        }

        return (uint)Math.Max(chosen, 0);
    }

    /// <summary>The name of a chosen device.</summary>
    /// <param name="deviceId">A device id from <see cref="Choose"/>.</param>
    /// <param name="names">The device names in device order.</param>
    /// <returns>The name; empty for the MIDI mapper.</returns>
    public static string NameOf(uint deviceId, IReadOnlyList<string> names) => deviceId < (uint)names.Count ? names[(int)deviceId] : "";

    /// <summary>
    /// How long a device takes to sound a message it was sent: <see cref="GsWavetableLatency"/> for the Microsoft GS
    /// Wavetable Synth; zero for other devices (a hardware port sends at once; what follows is <c>music.syncOffsetMs</c>).
    /// </summary>
    /// <param name="deviceName">The device name.</param>
    /// <returns>The latency.</returns>
    public static TimeSpan LatencyOf(string deviceName) =>
        deviceName.Contains(GsWavetableName, StringComparison.OrdinalIgnoreCase) ? GsWavetableLatency : TimeSpan.Zero;

    private static int FindIndex(IReadOnlyList<string> names, Func<string, bool> match)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (match(names[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ReadName(NativeMethods.MidiOutCaps caps)
    {
        ReadOnlySpan<char> name = caps.Name;
        int end = name.IndexOf('\0');
        return new string(end < 0 ? name : name[..end]).Trim();
    }
}
