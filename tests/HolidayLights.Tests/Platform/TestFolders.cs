using System.Diagnostics;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// Cleans up a test's private data root. A freshly written stand-in <c>HolidayLights.exe</c> can stay locked by the virus
/// scanner for a few seconds, so deletion is retried for a while instead of leaving the folder behind.
/// </summary>
internal static class TestFolders
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    public static void Delete(TempDataRoot data)
    {
        data.Dispose();
        long start = Stopwatch.GetTimestamp();
        while (Directory.Exists(data.Root) && Stopwatch.GetElapsedTime(start) < Patience)
        {
            Thread.Sleep(200);
            try
            {
                Directory.Delete(data.Root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still in use: try again shortly.
            }
        }
    }
}
