namespace HolidayLights.Tests.Shared;

/// <summary>
/// A private, empty data root for one test (owner: contracts): every user folder (settings, themes, caches, logs, holding
/// folder, My Bulbs, My Music, My Pictures) lives under a new temporary folder that is deleted on dispose. Bundled content
/// comes from the test output folder (the App's <c>Content</c>).
/// </summary>
public sealed class TempDataRoot : IDisposable
{
    /// <summary>Creates the folder.</summary>
    public TempDataRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "HolidayLightsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Paths = DataPaths.ForDataRoot(Root, TestPaths.OutputFolder);
    }

    /// <summary>The temporary data root.</summary>
    public string Root { get; }

    /// <summary>Data paths relocated under <see cref="Root"/>.</summary>
    public DataPaths Paths { get; }

    /// <summary>Deletes the folder and everything in it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file may still be open by a component under test; the OS temp cleanup removes it later.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
