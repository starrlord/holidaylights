namespace HolidayLights.Rendering.Threading;

/// <summary>Shell and topology notifications received by the hidden watcher window.</summary>
internal enum ShellEvent
{
    /// <summary><c>TaskbarCreated</c>: Explorer (re)started (also sent on some DPI changes).</summary>
    TaskbarCreated,

    /// <summary><c>WM_DISPLAYCHANGE</c>.</summary>
    DisplayChange,

    /// <summary><c>WM_SETTINGCHANGE(SPI_SETWORKAREA)</c>.</summary>
    WorkAreaChange,

    /// <summary><c>WM_DPICHANGED</c>.</summary>
    DpiChange,

    /// <summary><c>WM_POWERBROADCAST</c> resume.</summary>
    Resumed,
}

/// <summary>What the static window procedures and callbacks of the Lights thread report to its engine.</summary>
internal interface ILightsThreadSink
{
    /// <summary>A shell or topology notification arrived.</summary>
    void OnShellEvent(ShellEvent shellEvent);

    /// <summary>One of our windows was destroyed (for example because Explorer, its parent or owner, exited).</summary>
    void OnWindowDestroyed(nint hwnd);

    /// <summary>One of our top-level windows got <c>WM_DPICHANGED</c>.</summary>
    void OnWindowDpiChanged(nint hwnd);

    /// <summary>Explorer processed <c>0x052C</c> (the wallpaper WorkerW should exist now).</summary>
    void OnWorkerSpawned();

    /// <summary>The foreground window changed (<c>EVENT_SYSTEM_FOREGROUND</c>).</summary>
    void OnForegroundChanged(nint hwnd);

    /// <summary>A callback failed; the Lights thread treats it as a crash.</summary>
    void ReportFailure(Exception exception);
}

/// <summary>
/// Routes static window procedures and Win32 callbacks (which cannot capture state) to the engine of the current Lights
/// thread. Every callback runs on the thread that created the window or hook.
/// </summary>
internal static class LightsThreadContext
{
    [ThreadStatic]
    private static ILightsThreadSink? sink;

    /// <summary>The sink of the current thread, if any.</summary>
    public static ILightsThreadSink? Current => sink;

    /// <summary>Attaches the engine of the current thread.</summary>
    public static void Attach(ILightsThreadSink engine) => sink = engine;

    /// <summary>Detaches it (thread end).</summary>
    public static void Detach() => sink = null;

    /// <summary>Runs a callback body, reporting any exception to the sink instead of letting it reach native code.</summary>
    public static void Guard(Action<ILightsThreadSink> body)
    {
        ILightsThreadSink? current = sink;
        if (current is null)
        {
            return;
        }

        try
        {
            body(current);
        }
        catch (Exception exception)
        {
            current.ReportFailure(exception);
        }
    }
}
