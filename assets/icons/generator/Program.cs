using System.IO;

namespace HolidayLights.Branding;

/// <summary>
/// Entry point of the branding generator. Commands:
/// <list type="bullet">
/// <item><c>icons</c> - the application, tray and <c>.bul</c> document icons (<c>src/HolidayLights.App/Assets</c>) and
/// their 256 px masters (<c>assets/icons/png</c>).</item>
/// <item><c>illustrations</c> - <c>src/HolidayLights.App/Assets/Illustrations.xaml</c> (layer-mode cards and the taskbar
/// corner, light and dark).</item>
/// <item><c>banners</c> - the 5.4 About banner, light strips and help banner, MMPX-enlarged
/// (<c>src/HolidayLights.App/Assets/Banner</c>).</item>
/// <item><c>help-images</c> - the pictures of the Help topics (<c>src/HolidayLights.App/HelpContent/images</c>).</item>
/// <item><c>credits</c> - the add-on bulb artists, from the bundled bulb files, in <c>HelpContent/credits.json</c> and the
/// Art Copyright Information topic.</item>
/// <item><c>user-guide</c> - <c>docs/USER-GUIDE.md</c>, the Help topics as one document.</item>
/// <item><c>review &lt;folder&gt;</c> - review sheets of the art at 100 % and enlarged (not shipped).</item>
/// <item><c>all</c> - everything except <c>review</c>.</item>
/// </list>
/// Options: <c>--repo &lt;folder&gt;</c> (default: the folder above the current one that holds HolidayLights.sln).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string command = args.Length > 0 ? args[0] : "all";
            RepoPaths paths = RepoPaths.Find(OptionValue(args, "--repo"));
            switch (command)
            {
                case "icons":
                    IconGenerator.Run(paths);
                    break;
                case "illustrations":
                    IllustrationGenerator.Run(paths);
                    break;
                case "banners":
                    BannerGenerator.Run(paths);
                    break;
                case "help-images":
                    HelpImageGenerator.Run(paths);
                    break;
                case "credits":
                    ContentGenerator.Credits(paths);
                    break;
                case "user-guide":
                    ContentGenerator.UserGuide(paths);
                    break;
                case "review":
                    string folder = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
                        ? Path.GetFullPath(args[1])
                        : throw new ArgumentException("review needs an output folder.");
                    ReviewGenerator.Run(paths, folder);
                    break;
                case "all":
                    IconGenerator.Run(paths);
                    IllustrationGenerator.Run(paths);
                    BannerGenerator.Run(paths);
                    HelpImageGenerator.Run(paths);
                    ContentGenerator.Credits(paths);
                    ContentGenerator.UserGuide(paths);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command '{command}'. Use icons, illustrations, banners, help-images, credits, user-guide, review <folder> or all.");
                    return 2;
            }

            return 0;
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static string? OptionValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

/// <summary>Locations in the repository that the generator reads and writes.</summary>
/// <param name="Root">The repository root (the folder of HolidayLights.sln).</param>
internal sealed record RepoPaths(string Root)
{
    /// <summary>The App's branding folder (<c>src/HolidayLights.App/Assets</c>).</summary>
    public string AppAssets => Path.Combine(Root, "src", "HolidayLights.App", "Assets");

    /// <summary>The help content folder (<c>src/HolidayLights.App/HelpContent</c>).</summary>
    public string HelpContent => Path.Combine(Root, "src", "HolidayLights.App", "HelpContent");

    /// <summary>The icon masters folder (<c>assets/icons/png</c>).</summary>
    public string IconMasters => Path.Combine(Root, "assets", "icons", "png");

    /// <summary>The original 5.4 art (<c>assets/heritage</c>, read-only).</summary>
    public string Heritage => Path.Combine(Root, "assets", "heritage");

    /// <summary>The built-in bulb art (<c>assets/builtin</c>, read-only).</summary>
    public string BuiltIn => Path.Combine(Root, "assets", "builtin");

    /// <summary>Finds the repository root.</summary>
    /// <param name="explicitRoot">A root given on the command line, or null to search upwards from the current folder.</param>
    /// <returns>The paths.</returns>
    public static RepoPaths Find(string? explicitRoot)
    {
        if (explicitRoot is not null)
        {
            string full = Path.GetFullPath(explicitRoot);
            return File.Exists(Path.Combine(full, "HolidayLights.sln"))
                ? new RepoPaths(full)
                : throw new ArgumentException($"HolidayLights.sln not found in {full}.");
        }

        for (DirectoryInfo? dir = new(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HolidayLights.sln")))
            {
                return new RepoPaths(dir.FullName);
            }
        }

        throw new ArgumentException("Run the generator inside the repository or pass --repo <folder>.");
    }
}
