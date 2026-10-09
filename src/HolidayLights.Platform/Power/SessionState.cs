using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Power;

/// <summary>The lock state of this Windows session at start-up (later changes arrive through <c>SessionSwitch</c>).</summary>
internal static unsafe class SessionState
{
    /// <summary>
    /// Reads <c>WTSINFOEXW.Data.WTSInfoExLevel1.SessionFlags</c>: 0 = locked, 1 = unlocked (reversed only on Windows 7).
    /// The level-1 union is 8-byte aligned, so the flags sit at offset 16 (offset 12 would read the connect state).
    /// </summary>
    /// <returns>True when the session is locked; false when unlocked or unknown.</returns>
    public static bool IsLocked()
    {
        const int SessionFlagsOffset = 16;
        const int Locked = 0;
        if (!NativeMethods.WTSQuerySessionInformation(0, NativeMethods.WTS_CURRENT_SESSION, NativeMethods.WTS_SESSION_INFO_EX, out nint buffer, out uint size))
        {
            return false;
        }

        try
        {
            return size >= SessionFlagsOffset + sizeof(int) && *(int*)(buffer + SessionFlagsOffset) == Locked;
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
    }
}
