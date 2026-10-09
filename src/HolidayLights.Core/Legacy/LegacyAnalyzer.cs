namespace HolidayLights.Core.Legacy;

/// <summary>Reads a 5.4 installation without changing anything: registry values, add-on ids, content matches, files.</summary>
internal sealed class LegacyAnalyzer
{
    private readonly IBulbCatalog bulbs;
    private readonly ISongLibrary songs;
    private readonly IPictureLibrary pictures;
    private readonly IShellOperations shell;
    private readonly ILegacyBulbHeaderReader headers;

    /// <summary>Creates the analyzer.</summary>
    /// <param name="bulbs">Finds installed bulbs with the same content.</param>
    /// <param name="songs">Tells which songs are bundled.</param>
    /// <param name="pictures">Tells which pictures are bundled.</param>
    /// <param name="shell">Resolves shortcuts.</param>
    /// <param name="headers">Reads <c>.bul</c> headers.</param>
    public LegacyAnalyzer(IBulbCatalog bulbs, ISongLibrary songs, IPictureLibrary pictures, IShellOperations shell, ILegacyBulbHeaderReader headers)
    {
        this.bulbs = bulbs;
        this.songs = songs;
        this.pictures = pictures;
        this.shell = shell;
        this.headers = headers;
    }

    /// <summary>Analyzes a snapshot.</summary>
    /// <param name="snapshot">The 5.4 registry.</param>
    /// <returns>The analysis.</returns>
    public LegacyAnalysis Analyze(LegacyRegistrySnapshot snapshot)
    {
        LegacyAddOnFiles addOns = LegacyAddOnFiles.Scan(snapshot.ProgramFolder, headers);
        var matches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyAddOn addOn in addOns.Loaded)
        {
            if (bulbs.FindByContent(addOn.FilePath) is { } id)
            {
                matches[addOn.FilePath] = id;
            }
        }

        return new LegacyAnalysis
        {
            Snapshot = snapshot,
            Main = new LegacyValueSet(snapshot.Values),
            AddOns = addOns,
            ContentMatches = matches,
            Songs = ListSongs(snapshot.ProgramFolder),
            Pictures = ListPictures(snapshot.ProgramFolder),
            IsFactoryDefault = LegacyFactoryDefaults.IsFactoryDefault(snapshot),
        };
    }

    /// <summary>True when two files have the same bytes.</summary>
    /// <param name="a">The first file.</param>
    /// <param name="b">The second file.</param>
    /// <returns>True when equal; false when different or unreadable.</returns>
    public static bool SameContent(string a, string b)
    {
        try
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            return infoA.Exists && infoB.Exists && infoA.Length == infoB.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The songs 5.4 listed: supported suffixes, judged on a shortcut's target.</summary>
    private IReadOnlyList<LegacyMediaFile> ListSongs(string? programFolder)
    {
        var result = new List<LegacyMediaFile>();
        foreach (string path in LegacyFolders.ListFiles(programFolder, LegacyFolders.Music, "*"))
        {
            string? target = Resolve(path);
            if (target is not null && LegacyFolders.IsMusic(target))
            {
                string? bundled = !IsShortcut(path) && songs.TryGetSong(MediaIds.Bundled(Path.GetFileName(path)), out SongInfo? song)
                    && song.Origin == MediaOrigin.Bundled && SameContent(path, song.FilePath) ? song.Id : null;
                result.Add(new LegacyMediaFile(path, target, bundled));
            }
        }

        return result;
    }

    /// <summary>The pictures 5.4 listed: every file (it had no extension filter).</summary>
    private IReadOnlyList<LegacyMediaFile> ListPictures(string? programFolder)
    {
        var result = new List<LegacyMediaFile>();
        foreach (string path in LegacyFolders.ListFiles(programFolder, LegacyFolders.Pictures, "*"))
        {
            string? bundled = !IsShortcut(path) && pictures.TryGetPicture(MediaIds.Bundled(Path.GetFileName(path)), out PictureInfo? picture)
                && picture.Origin == MediaOrigin.Bundled && SameContent(path, picture.FilePath) ? picture.Id : null;
            result.Add(new LegacyMediaFile(path, Resolve(path), bundled));
        }

        return result;
    }

    private string? Resolve(string path) => IsShortcut(path) ? shell.ResolveShortcut(path) : path;

    private static bool IsShortcut(string path) => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
}
