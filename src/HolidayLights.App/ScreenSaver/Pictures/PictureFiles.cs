using System.IO;

namespace HolidayLights.App.ScreenSaver.Pictures;

/// <summary>The picture files of the bundled and My Pictures folders (PRODUCT-SPEC 3.5.6).</summary>
internal static class PictureFiles
{
    /// <summary>The types "Add..." accepts ("Picture Files": <c>*.bmp; *.jpg; *.jpeg; *.gif; *.png</c>).</summary>
    public static IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>([".bmp", ".jpg", ".jpeg", ".gif", ".png"], StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a supported picture file name.</summary>
    /// <param name="path">A path or file name.</param>
    /// <returns>True when its extension is a picture type.</returns>
    public static bool IsPicture(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Lists the pictures of a folder: supported files that are not hidden, system or temporary (5.4 skipped those), A-Z
    /// by title. A missing or unreadable folder has none.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="origin">Bundled or My Pictures (it makes the ids).</param>
    /// <returns>The pictures.</returns>
    public static IReadOnlyList<PictureInfo> Scan(string folder, MediaOrigin origin)
    {
        var pictures = new List<PictureInfo>();
        try
        {
            if (!Directory.Exists(folder))
            {
                return pictures;
            }

            const FileAttributes Skipped = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary | FileAttributes.Directory;
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles())
            {
                if ((file.Attributes & Skipped) == 0 && IsPicture(file.Name))
                {
                    string id = origin == MediaOrigin.Bundled ? MediaIds.Bundled(file.Name) : MediaIds.User(file.Name);
                    pictures.Add(new PictureInfo(id, Path.GetFileNameWithoutExtension(file.Name), file.FullName, origin));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder lists no pictures; the next scan tries again.
        }

        pictures.Sort(Order);
        return pictures;
    }

    /// <summary>A-Z by title (the user's language), then by file name.</summary>
    private static int Order(PictureInfo a, PictureInfo b)
    {
        int byTitle = StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title);
        return byTitle != 0 ? byTitle : string.CompareOrdinal(a.FilePath, b.FilePath);
    }
}
