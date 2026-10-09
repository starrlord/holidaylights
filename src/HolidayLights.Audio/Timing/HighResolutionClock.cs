using System.ComponentModel;
using System.Diagnostics;
using HolidayLights.Audio.Native;
using Microsoft.Win32.SafeHandles;

namespace HolidayLights.Audio.Timing;

/// <summary>
/// Stopwatch time with waits paced by a <c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c> timer (0.34 ms mean lateness on the
/// reference PC; unlike <c>timeBeginPeriod</c> it keeps its resolution when every window
/// of the process is hidden). One instance serves one waiting thread; <see cref="Now"/> may be read anywhere. The timer is
/// created by the first wait, so constructing a clock never fails.
/// </summary>
internal sealed class HighResolutionClock : IMusicClock, IDisposable
{
    private const long HundredNanosecondsPerSecond = 10_000_000;

    private readonly WaitHandle[] handles = new WaitHandle[2];
    private WaitableTimer? timer;

    /// <inheritdoc />
    public long Frequency => Stopwatch.Frequency;

    /// <inheritdoc />
    public long Now => Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public bool WaitUntil(long timestamp, WaitHandle wake)
    {
        if (timestamp == long.MaxValue)
        {
            wake.WaitOne();
            return false;
        }

        WaitableTimer waitable = timer ??= new WaitableTimer();
        handles[0] = waitable;
        handles[1] = wake;
        while (true)
        {
            long remaining = timestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return true;
            }

            waitable.Start(Math.Max(1, (long)((Int128)remaining * HundredNanosecondsPerSecond / Stopwatch.Frequency)));
            if (WaitHandle.WaitAny(handles) == 1)
            {
                return false;
            }
        }
    }

    /// <summary>Closes the timer.</summary>
    public void Dispose() => timer?.Dispose();

    /// <summary>A one-shot waitable timer, high resolution when Windows offers it.</summary>
    private sealed unsafe class WaitableTimer : WaitHandle
    {
        public WaitableTimer()
        {
            nint handle = NativeMethods.CreateWaitableTimerEx(
                0, null, NativeMethods.CreateWaitableTimerHighResolution, NativeMethods.TimerAllAccess);
            if (handle == 0)
            {
                handle = NativeMethods.CreateWaitableTimerEx(0, null, 0, NativeMethods.TimerAllAccess);
            }

            if (handle == 0)
            {
                throw new Win32Exception();
            }

            SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
        }

        /// <summary>Arms the timer.</summary>
        /// <param name="hundredNanoseconds">Relative due time.</param>
        public void Start(long hundredNanoseconds)
        {
            long dueTime = -hundredNanoseconds;
            if (!NativeMethods.SetWaitableTimer(SafeWaitHandle, &dueTime, 0, 0, 0, resume: false))
            {
                throw new Win32Exception();
            }
        }
    }
}
