using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace HolidayLights.Core.Themes;

/// <summary>
/// Theme export and import as a <c>.hltheme</c> file (PRODUCT-SPEC 6.4.4, [NICE] N4): a zip with the theme
/// (<c>theme.json</c>, schema <c>holidaylights.theme/1</c>) and the My Bulbs files (<c>bulbs/</c>) and My Pictures
/// picture (<c>pictures/</c>) it uses, so a theme can be shared with family without a server. Bundled and built-in bulbs,
/// pictures and every song are not packed: every installation has the bundled ones, and songs named by a theme but missing
/// are ignored when it is loaded.
/// </summary>
public static class ThemePackage
{
    /// <summary>The file extension.</summary>
    public const string Extension = ".hltheme";

    private const string ThemeEntry = "theme.json";
    private const string BulbFolder = "bulbs/";
    private const string PictureFolder = "pictures/";
    private const long MaxEntryBytes = 32L * 1024 * 1024;
    private const long MaxTotalBytes = 128L * 1024 * 1024;
    private const int MaxEntries = 64;

    private static readonly string[] PictureExtensions = [".bmp", ".jpg", ".jpeg", ".gif", ".png"];

    /// <summary>Writes a theme and the user files it uses to a package (atomically; an existing file is replaced).</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="destination">The <c>.hltheme</c> file.</param>
    /// <param name="paths">Where the user's bulbs and pictures are.</param>
    /// <exception cref="IOException">The package could not be written.</exception>
    public static void Export(ThemeDefinition theme, string destination, DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(paths);
        string temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(ThemeEntry).Open(), new UTF8Encoding(false)))
                {
                    writer.Write(HolidayLightsJson.Serialize(theme));
                }

                foreach (string stem in UserBulbStems(theme))
                {
                    AddFile(zip, Path.Combine(paths.MyBulbsFolder, stem + ".bul"), BulbFolder + stem + ".bul");
                }

                if (UserPicture(theme) is { } picture)
                {
                    AddFile(zip, Path.Combine(paths.MyPicturesFolder, picture), PictureFolder + picture);
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    /// <summary>
    /// Reads a package: its bulbs go through the bulb catalog (equal content is recognized, names are made unique) and its
    /// picture through the picture library, and the theme's ids are changed to the ids the files got. The theme is not
    /// saved: the caller checks its name ("Replace Theme?") and saves it.
    /// </summary>
    /// <param name="package">The <c>.hltheme</c> file.</param>
    /// <param name="paths">The data paths (the files are unpacked in the cache folder).</param>
    /// <param name="bulbs">Adds the bulb files to My Bulbs.</param>
    /// <param name="pictures">Adds the picture to My Pictures.</param>
    /// <returns>The theme, normalized, with the ids of the added files.</returns>
    /// <exception cref="InvalidDataException">The file is not a Holiday Lights theme.</exception>
    /// <exception cref="IOException">The file could not be read or unpacked.</exception>
    public static ThemeDefinition Import(string package, DataPaths paths, IBulbCatalog bulbs, IPictureLibrary pictures)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(pictures);
        string staging = Path.Combine(paths.CacheFolder, "ThemeImport", Guid.NewGuid().ToString("N"));
        try
        {
            using ZipArchive zip = OpenPackage(package);
            ThemeDefinition theme = ReadTheme(zip);
            var bulbIds = new Dictionary<string, string>(BulbIds.Comparer);
            var pictureIds = new Dictionary<string, string>(MediaIds.Comparer);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (FileOf(entry, BulbFolder, [".bul"]) is { } bulbFile && bulbs.ImportFile(Extract(entry, staging, bulbFile)).BulbId is { } bulbId)
                {
                    bulbIds[BulbIds.User(Path.GetFileNameWithoutExtension(bulbFile))] = bulbId;
                }
                else if (FileOf(entry, PictureFolder, PictureExtensions) is { } pictureFile
                    && pictures.AddFiles([Extract(entry, staging, pictureFile)]) is [{ Id: { } pictureId }])
                {
                    pictureIds[MediaIds.User(pictureFile)] = pictureId;
                }
            }

            return Remap(theme, bulbIds, pictureIds);
        }
        finally
        {
            DeleteStaging(staging);
        }
    }

    /// <summary>Opens the zip (a file that is no zip throws <see cref="InvalidDataException"/>) and checks its size.</summary>
    private static ZipArchive OpenPackage(string package)
    {
        ZipArchive zip = ZipFile.OpenRead(package);
        if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(e => e.Length) > MaxTotalBytes)
        {
            zip.Dispose();
            throw new InvalidDataException("The theme file is too large.");
        }

        return zip;
    }

    private static ThemeDefinition ReadTheme(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.GetEntry(ThemeEntry) is { Length: <= MaxEntryBytes } found
            ? found
            : throw new InvalidDataException("The file is not a Holiday Lights theme.");
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            ThemeDefinition theme = HolidayLightsJson.DeserializeTheme(reader.ReadToEnd());
            if (theme.Schema?.StartsWith("holidaylights.theme/", StringComparison.Ordinal) != true)
            {
                throw new InvalidDataException("The file is not a Holiday Lights theme.");
            }

            return ThemeNormalizer.Normalize(theme, ThemeNames.MakeValid(theme.Name));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The theme in the file is damaged.", e);
        }
    }

    /// <summary>The file name of an entry directly inside a folder of the package with an allowed extension, else null (no paths).</summary>
    private static string? FileOf(ZipArchiveEntry entry, string folder, string[] extensions)
    {
        string name = entry.FullName.Replace('\\', '/');
        if (!name.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || entry.Length > MaxEntryBytes)
        {
            return null;
        }

        string file = name[folder.Length..];
        return file.Length > 0 && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && file.Trim('.').Length > 0
            && extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
            ? file
            : null;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The temporary file is left behind; it never replaces the package.
        }
    }

    /// <summary>Removes the unpacked files (the library calls copied them); a locked leftover stays in the cache folder.</summary>
    private static void DeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unpacked copies in the cache folder are harmless.
        }
    }

    private static string Extract(ZipArchiveEntry entry, string staging, string file)
    {
        string folder = Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Combine(folder, file);
        entry.ExtractToFile(path);
        return path;
    }

    private static void AddFile(ZipArchive zip, string source, string entryName)
    {
        if (File.Exists(source))
        {
            zip.CreateEntryFromFile(source, entryName);
        }
    }

    private static IEnumerable<string> UserBulbStems(ThemeDefinition theme)
    {
        IEnumerable<string> ids = theme.Arrangement?.EnumerateBulbIds() ?? [];
        if (theme.Saver?.Animation is { } animation && SaverAnimations.TryGetBulbId(animation, out string? saverBulb))
        {
            ids = ids.Append(saverBulb);
        }

        return ids.Where(id => BulbIds.TryGetOrigin(id, out BulbOrigin origin) && origin == BulbOrigin.UserAddOn)
            .Select(BulbIds.GetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? UserPicture(ThemeDefinition theme) =>
        MediaIds.TryParse(theme.Saver?.Picture, out MediaOrigin origin, out string file) && origin == MediaOrigin.User ? file : null;

    private static ThemeDefinition Remap(ThemeDefinition theme, Dictionary<string, string> bulbIds, Dictionary<string, string> pictureIds)
    {
        string Bulb(string id) => bulbIds.GetValueOrDefault(id, id);
        string? Corner(string? id) => id is null ? null : Bulb(id);
        SlotAssignment? arrangement = theme.Arrangement is not { } a ? null : new SlotAssignment
        {
            Top = [.. a.Top.Select(Bulb)],
            Right = [.. a.Right.Select(Bulb)],
            Bottom = [.. a.Bottom.Select(Bulb)],
            Left = [.. a.Left.Select(Bulb)],
            TopLeft = Corner(a.TopLeft),
            TopRight = Corner(a.TopRight),
            BottomLeft = Corner(a.BottomLeft),
            BottomRight = Corner(a.BottomRight),
        };
        ThemeSaver? saver = theme.Saver is not { } s ? null : s with
        {
            Animation = s.Animation is { } animation && SaverAnimations.TryGetBulbId(animation, out string? saverBulb) ? SaverAnimations.ForBulb(Bulb(saverBulb)) : s.Animation,
            Picture = s.Picture is { } picture ? pictureIds.GetValueOrDefault(picture, picture) : null,
        };
        return theme with { Arrangement = arrangement, Saver = saver };
    }
}
