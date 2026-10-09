using System.Windows.Threading;
using HolidayLights.App.ScreenSaver.Native;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// Calls a frame callback on the UI thread once per refresh of the compositor (Windows 11 <c>DCompositionWaitForCompositorClock</c>),
/// so Smooth Motion keeps pace with any refresh rate. A frame is only queued when the previous one has run: a busy UI
/// thread skips refreshes instead of piling up work.
/// </summary>
internal sealed class FramePacer : IDisposable
{
    private const uint WaitTimeoutMilliseconds = 250;

    private readonly Dispatcher dispatcher;
    private readonly Action frame;
    private readonly ManualResetEvent stop = new(false);
    private readonly Thread thread;
    private volatile bool stopped;
    private int queued;

    /// <summary>Starts calling <paramref name="frame"/> on the dispatcher's thread every compositor frame.</summary>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    /// <param name="frame">The frame callback.</param>
    public FramePacer(Dispatcher dispatcher, Action frame)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(frame);
        this.dispatcher = dispatcher;
        this.frame = frame;
        thread = new Thread(WaitForFrames) { IsBackground = true, Name = "Holiday Lights Saver Frames", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>Stops the frames (waits for the pacing thread to finish).</summary>
    public void Dispose()
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        stop.Set();
        thread.Join();
        stop.Dispose();
    }

    private void WaitForFrames()
    {
        nint[] handles = [stop.SafeWaitHandle.DangerousGetHandle()];
        while (true)
        {
            uint result = SaverNativeMethods.DCompositionWaitForCompositorClock(1, handles, WaitTimeoutMilliseconds);
            if (result == SaverNativeMethods.WAIT_OBJECT_0 || stopped)
            {
                return;
            }

            if (result == SaverNativeMethods.WAIT_OBJECT_0 + 1 && Interlocked.Exchange(ref queued, 1) == 0)
            {
                dispatcher.BeginInvoke(new Action(RunFrame), DispatcherPriority.Render);
            }
        }
    }

    private void RunFrame()
    {
        Volatile.Write(ref queued, 0);
        if (!stopped)
        {
            frame();
        }
    }
}
