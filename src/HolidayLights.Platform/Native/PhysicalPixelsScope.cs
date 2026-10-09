namespace HolidayLights.Platform.Native;

/// <summary>
/// Makes the calling thread per-monitor DPI aware (V2) until disposed, so monitor, cursor and window coordinates are
/// physical pixels whatever the process awareness is (the app is PerMonitorV2 by manifest; test hosts may not be).
/// </summary>
internal readonly struct PhysicalPixelsScope : IDisposable
{
    private readonly nint previous;

    private PhysicalPixelsScope(nint previous) => this.previous = previous;

    /// <summary>Switches the thread to per-monitor V2 awareness.</summary>
    /// <returns>The scope; dispose it to restore the previous awareness.</returns>
    public static PhysicalPixelsScope Enter() =>
        new(NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));

    /// <summary>Restores the previous awareness.</summary>
    public void Dispose()
    {
        if (previous != 0)
        {
            NativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }
}
