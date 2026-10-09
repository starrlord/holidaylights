using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Reads <c>GUID_SESSION_DISPLAY_STATUS</c> (delivered at once on registration, no window needed). While the displays are off
/// DWM composes only when content changes and evaluates DirectComposition animations at their end, so captures show static
/// values only: the animation checks need the displays on.
/// </summary>
internal static unsafe partial class DisplayPower
{
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private static int state = -1;

    /// <summary>True when the session's displays are on (or dimmed); false when they are off or the state is unknown.</summary>
    public static bool IsDisplayOn()
    {
        Guid setting = SessionDisplayStatus;
        var recipient = new SubscribeParameters { Callback = &OnSetting, Context = 0 };
        nint handle = 0;
        Volatile.Write(ref state, -1);
        if (PowerSettingRegisterNotification(&setting, 2, &recipient, &handle) != 0)
        {
            return false;
        }

        try
        {
            for (int i = 0; i < 50 && Volatile.Read(ref state) < 0; i++)
            {
                Thread.Sleep(10);
            }

            return Volatile.Read(ref state) is 1 or 2;
        }
        finally
        {
            PowerSettingUnregisterNotification(handle);
        }
    }

    /// <summary>
    /// Turns the displays on the way a nudge of the mouse does: one relative mouse move of zero pixels (the pointer stays
    /// where it is, nothing is clicked), which resets the idle timer. Waits up to 3 s for the displays.
    /// </summary>
    /// <returns>True when the displays are on.</returns>
    public static bool Wake()
    {
        if (IsDisplayOn())
        {
            return true;
        }

        var input = new INPUT { Type = InputMouse, Mouse = new MOUSEINPUT { Flags = MouseMove } };
        SendInput(1, &input, sizeof(INPUT));
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(100);
            if (IsDisplayOn())
            {
                return true;
            }
        }

        return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnSetting(nint context, uint type, nint setting)
    {
        uint length = *(uint*)(setting + 16);
        Volatile.Write(ref state, length >= 4 ? *(int*)(setting + 20) : 0);
        return 0;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingRegisterNotification(Guid* setting, uint flags, SubscribeParameters* recipient, nint* handle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingUnregisterNotification(nint handle);

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, INPUT* inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct SubscribeParameters
    {
        public delegate* unmanaged[Stdcall]<nint, uint, nint, uint> Callback;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public MOUSEINPUT Mouse;
    }
}

/// <summary>
/// A live test that needs the displays on (animations): skipped otherwise. With <c>HOLIDAYLIGHTS_LIVE_WAKE_DISPLAYS=1</c>
/// it first turns sleeping displays on with a zero-pixel mouse nudge (<see cref="DisplayPower.Wake"/>).
/// </summary>
public sealed class LiveDisplayFactAttribute : FactAttribute
{
    public LiveDisplayFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HOLIDAYLIGHTS_LIVE_TESTS") != "1")
        {
            Skip = "Live desktop test: set HOLIDAYLIGHTS_LIVE_TESTS=1 to run it.";
        }
        else if (!(Environment.GetEnvironmentVariable("HOLIDAYLIGHTS_LIVE_WAKE_DISPLAYS") == "1" ? DisplayPower.Wake() : DisplayPower.IsDisplayOn()))
        {
            Skip = "The displays are off: DWM presents no frames, so animations cannot be observed.";
        }
    }
}
