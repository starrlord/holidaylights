namespace HolidayLights.App.Shell;

/// <summary>Tells whether the normal Holiday Lights program runs, without becoming it (the single-instance mutex is only opened).</summary>
public static class RunningInstance
{
    /// <summary>
    /// True while a normal instance of this session holds its mutex (<see cref="InstanceNames.MutexFor"/>): with a data root
    /// only an instance on the same data root counts.
    /// </summary>
    /// <param name="dataRoot">The data root (<see cref="DataPaths.DataRoot"/>), or null for the normal locations.</param>
    /// <returns>True when Holiday Lights is running.</returns>
    public static bool IsPresent(string? dataRoot)
    {
        try
        {
            if (Mutex.TryOpenExisting(InstanceNames.MutexFor(dataRoot), out Mutex? existing))
            {
                existing.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance's mutex exists but cannot be opened: it is running.
            return true;
        }
    }

    /// <summary>Waits until the running instance has ended.</summary>
    /// <param name="dataRoot">The data root, or null for the normal locations.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>True when no instance runs any more.</returns>
    public static bool WaitForExit(string? dataRoot, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (IsPresent(dataRoot))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(100);
        }

        return true;
    }
}
