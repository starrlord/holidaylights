using System.ComponentModel;
using System.Diagnostics;
using HolidayLights.Rendering.Interop;

namespace HolidayLights.Rendering.Threading;

/// <summary>
/// How the Lights thread sleeps (<c>Snippets/RenderLoop.cs</c>): <c>MsgWaitForMultipleObjectsEx</c> on a high-resolution
/// waitable timer (the next deadline: a step boundary, a fade end, the 2 s maintenance) and an event set when a command is
/// queued, waking early for window messages. Nothing else ever blocks the thread.
/// </summary>
internal sealed unsafe class LightsWaiter : IDisposable
{
    private readonly nint commandEvent;
    private readonly nint timer;

    /// <summary>Creates the event and the timer (high resolution when Windows supports it).</summary>
    public LightsWaiter()
    {
        commandEvent = Kernel32.CreateEventExW(0, null, 0, Win32Constants.EventAllAccess);
        if (commandEvent == 0)
        {
            throw new Win32Exception();
        }

        timer = Kernel32.CreateWaitableTimerExW(0, null, Win32Constants.CreateWaitableTimerHighResolution, Win32Constants.TimerAllAccess);
        if (timer == 0)
        {
            timer = Kernel32.CreateWaitableTimerExW(0, null, 0, Win32Constants.TimerAllAccess);
        }

        if (timer == 0)
        {
            Kernel32.CloseHandle(commandEvent);
            throw new Win32Exception();
        }
    }

    /// <summary>Wakes the thread (any thread may call it).</summary>
    public void Signal() => Kernel32.SetEvent(commandEvent);

    /// <summary>Sleeps until <paramref name="deadline"/> (a <see cref="Stopwatch"/> timestamp; null = no deadline), a command or a message.</summary>
    public void Wait(long? deadline)
    {
        if (deadline is { } due)
        {
            long remaining = due - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }

            long relative = -Math.Max(1, (long)(remaining * 10_000_000.0 / Stopwatch.Frequency));
            Kernel32.SetWaitableTimer(timer, &relative, 0, 0, 0, false);
        }
        else
        {
            Kernel32.CancelWaitableTimer(timer);
        }

        nint* handles = stackalloc nint[2];
        handles[0] = commandEvent;
        handles[1] = timer;
        User32.MsgWaitForMultipleObjectsEx(2, handles, Win32Constants.Infinite, Win32Constants.QsAllInput, Win32Constants.MwmoInputAvailable);
    }

    /// <summary>Closes the handles.</summary>
    public void Dispose()
    {
        Kernel32.CloseHandle(timer);
        Kernel32.CloseHandle(commandEvent);
    }
}
