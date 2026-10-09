using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HolidayLights.Audio.Native;

/// <summary>The WinMM and kernel32 imports of the music engine (signatures verified on Windows 11).</summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>The MIDI mapper pseudo device (used only when no MIDI output device exists).</summary>
    internal const uint MidiMapper = 0xFFFFFFFF;

    /// <summary><c>MMSYSERR_NOERROR</c>.</summary>
    internal const uint NoError = 0;

    /// <summary><c>MMSYSERR_ALLOCATED</c>: the device is already in use (the GS synth allows one handle).</summary>
    internal const uint ErrorAllocated = 4;

    /// <summary><c>MIDIERR_STILLPLAYING</c>: a header is still queued.</summary>
    internal const uint ErrorStillPlaying = 65;

    /// <summary><c>MHDR_DONE</c>: the driver finished with a SysEx buffer.</summary>
    internal const uint HeaderDone = 0x1;

    /// <summary><c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c> (Windows 10 1803+).</summary>
    internal const uint CreateWaitableTimerHighResolution = 0x2;

    /// <summary><c>TIMER_ALL_ACCESS</c>.</summary>
    internal const uint TimerAllAccess = 0x1F0003;

    private const int ThreadPowerThrottling = 3;
    private const uint ThreadPowerThrottlingCurrentVersion = 1;
    private const uint ThreadPowerThrottlingExecutionSpeed = 0x1;

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutGetNumDevs();

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutGetDevCapsW(nuint deviceId, MidiOutCaps* caps, uint size);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutOpen(nint* handle, uint deviceId, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutClose(nint handle);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutReset(nint handle);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutShortMsg(nint handle, uint message);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutPrepareHeader(nint handle, MidiHeader* header, uint size);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutUnprepareHeader(nint handle, MidiHeader* header, uint size);

    [LibraryImport("winmm.dll")]
    internal static partial uint midiOutLongMsg(nint handle, MidiHeader* header, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    internal static partial nint CreateWaitableTimerEx(nint attributes, char* name, uint flags, uint desiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWaitableTimer(
        SafeWaitHandle timer, long* dueTime, int period, nint completionRoutine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadInformation(nint thread, int informationClass, void* information, uint size);

    /// <summary>
    /// Opts the calling thread out of EcoQoS power throttling (PRODUCT-SPEC 6.1.3: never while music plays), so Windows
    /// keeps the music threads on full-speed cores even when every window is hidden. Failure is harmless and ignored.
    /// </summary>
    internal static void DisablePowerThrottlingForCurrentThread()
    {
        var state = new ThreadPowerThrottlingState
        {
            Version = ThreadPowerThrottlingCurrentVersion,
            ControlMask = ThreadPowerThrottlingExecutionSpeed,
            StateMask = 0,
        };
        SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, &state, (uint)sizeof(ThreadPowerThrottlingState));
    }

    /// <summary><c>MIDIOUTCAPSW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        public NameBuffer Name;
        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    /// <summary>The 32 UTF-16 characters of <c>szPname</c>.</summary>
    [InlineArray(32)]
    internal struct NameBuffer
    {
        private char element;
    }

    /// <summary><c>MIDIHDR</c> (64-bit and 32-bit layouts follow from the pointer-sized fields).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MidiHeader
    {
        public byte* Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public MidiHeader* Next;
        public nint Reserved;
        public uint Offset;
        public ReservedBuffer ReservedArray;
    }

    /// <summary>The eight <c>DWORD_PTR dwReserved</c> values of <c>MIDIHDR</c>.</summary>
    [InlineArray(8)]
    internal struct ReservedBuffer
    {
        private nint element;
    }

    /// <summary><c>THREAD_POWER_THROTTLING_STATE</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }
}
