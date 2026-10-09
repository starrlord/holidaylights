namespace HolidayLights.Tests.Shared;

/// <summary>Locations of repository data for tests (owner: contracts).</summary>
public static class TestPaths
{
    private static readonly Lazy<string> RepoRootPath = new(FindRepoRoot);

    /// <summary>The repository root (the folder of <c>HolidayLights.sln</c>).</summary>
    public static string RepoRoot => RepoRootPath.Value;

    /// <summary>The test output folder (contains <c>Golden\</c> and the App's <c>Content\</c>).</summary>
    public static string OutputFolder => AppContext.BaseDirectory;

    /// <summary>A golden file copied to the output folder (<c>tests/HolidayLights.Tests/Golden</c>).</summary>
    /// <param name="relativePath">Path relative to <c>Golden</c>, e.g. <c>layout/default.json.gz</c>.</param>
    /// <returns>The full path.</returns>
    public static string Golden(string relativePath) => Path.Combine(OutputFolder, "Golden", relativePath);

    /// <summary>A test input file copied to the output folder (<c>tests/HolidayLights.Tests/Fixtures</c>).</summary>
    /// <param name="relativePath">Path relative to <c>Fixtures</c>, e.g. <c>Music/reset.mid</c>.</param>
    /// <returns>The full path.</returns>
    public static string Fixture(string relativePath) => Path.Combine(OutputFolder, "Fixtures", relativePath);

    /// <summary>The bundled content folder of the App output, copied next to the tests (<c>Content\Bulbs</c>, <c>Content\Music</c>, <c>Content\Pictures</c>).</summary>
    public static string ContentFolder => Path.Combine(OutputFolder, "Content");

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HolidayLights.sln")))
            {
                return dir.FullName;
            }
        }

        // The build root can be outside the repository (HL_BUILD_ROOT); fall back to the source location of this file.
        string? fromSource = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SourceFile()))));
        return fromSource is not null && File.Exists(Path.Combine(fromSource, "HolidayLights.sln"))
            ? fromSource
            : throw new DirectoryNotFoundException("HolidayLights.sln not found above the test output or the source tree.");
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
