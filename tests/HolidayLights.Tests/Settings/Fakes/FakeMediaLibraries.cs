using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings.Fakes;

/// <summary>A song library over a bundled folder and a My Music folder (copies files; no watching, no hiding).</summary>
internal sealed class FakeSongLibrary : ISongLibrary
{
    private readonly List<SongInfo> songs = [];
    private readonly string? myMusic;

    /// <summary>Creates a library with the bundled songs of a folder.</summary>
    /// <param name="bundledFolder">The bundled songs (e.g. the test output's <c>Content\Music</c>), or null for none.</param>
    /// <param name="myMusic">Where added songs are copied, or null.</param>
    public FakeSongLibrary(string? bundledFolder = null, string? myMusic = null)
    {
        this.myMusic = myMusic;
        foreach (string file in bundledFolder is null ? Enumerable.Empty<string>() : Directory.EnumerateFiles(bundledFolder, "*.mid").Order(StringComparer.OrdinalIgnoreCase))
        {
            songs.Add(Song(MediaIds.Bundled(Path.GetFileName(file)), file, MediaOrigin.Bundled));
        }
    }

    /// <summary>The bundled songs of the test output (<c>Content\Music</c>).</summary>
    public static FakeSongLibrary Bundled(string? myMusic = null) => new(Path.Combine(TestPaths.ContentFolder, "Music"), myMusic);

    public event EventHandler? Changed;

    public IReadOnlyList<SongInfo> Songs => songs;

    public IReadOnlyList<SongInfo> HiddenSongs => [];

    public List<string> AddedSources { get; } = [];

    public void Start()
    {
    }

    public bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? song)
    {
        song = songs.FirstOrDefault(s => MediaIds.Comparer.Equals(s.Id, id));
        return song is not null;
    }

    public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) =>
        [.. paths.Select(path => MediaFiles.Add(path, myMusic, [".mid", ".rmi", ".mp3", ".wav", ".wma", ".lnk"], AddedSources, songs, (id, file) => Song(id, file, MediaOrigin.User), s => s.FilePath))];

    public HeldItem? Remove(string id) => throw new NotSupportedException();

    public void Restore(HeldItem item) => throw new NotSupportedException();

    public void RestoreHiddenSongs() => Changed?.Invoke(this, EventArgs.Empty);

    private static SongInfo Song(string id, string file, MediaOrigin origin) => new()
    {
        Id = id,
        Title = Path.GetFileNameWithoutExtension(file),
        FilePath = file,
        Origin = origin,
        Kind = SongKind.Midi,
        SortKey = Path.GetFileName(file).ToUpperInvariant(),
    };
}

/// <summary>A picture library over a bundled folder and a My Pictures folder.</summary>
internal sealed class FakePictureLibrary : IPictureLibrary
{
    private readonly List<PictureInfo> pictures = [];
    private readonly string? myPictures;

    /// <summary>Creates a library with the bundled pictures of a folder.</summary>
    /// <param name="bundledFolder">The bundled pictures, or null for none.</param>
    /// <param name="myPictures">Where added pictures are copied, or null.</param>
    public FakePictureLibrary(string? bundledFolder = null, string? myPictures = null)
    {
        this.myPictures = myPictures;
        foreach (string file in bundledFolder is null ? Enumerable.Empty<string>() : Directory.EnumerateFiles(bundledFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            pictures.Add(new PictureInfo(MediaIds.Bundled(Path.GetFileName(file)), Path.GetFileNameWithoutExtension(file), file, MediaOrigin.Bundled));
        }
    }

    /// <summary>The bundled pictures of the test output (<c>Content\Pictures</c>).</summary>
    public static FakePictureLibrary Bundled(string? myPictures = null) => new(Path.Combine(TestPaths.ContentFolder, "Pictures"), myPictures);

    public event EventHandler? Changed;

    public IReadOnlyList<PictureInfo> Pictures => pictures;

    public List<string> AddedSources { get; } = [];

    public void Start()
    {
    }

    public bool TryGetPicture(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PictureInfo? picture)
    {
        picture = pictures.FirstOrDefault(p => MediaIds.Comparer.Equals(p.Id, id));
        return picture is not null;
    }

    public Rgba32Image? LoadImage(string id) => throw new NotSupportedException();

    public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) =>
        [.. paths.Select(path => MediaFiles.Add(path, myPictures, [".bmp", ".jpg", ".jpeg", ".gif", ".png"], AddedSources, pictures,
            (id, file) => new PictureInfo(id, Path.GetFileNameWithoutExtension(file), file, MediaOrigin.User), p => p.FilePath))];

    public HeldItem? Remove(string id) => throw new NotSupportedException();

    public void Restore(HeldItem item) => throw new NotSupportedException();

    public void RestoreHiddenPictures() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>The copy rules shared by the fake libraries: same content is "already present", a taken name gets " (2)".</summary>
internal static class MediaFiles
{
    public static MediaImportResult Add<T>(string path, string? folder, string[] extensions, List<string> added, List<T> items, Func<string, string, T> create, Func<T, string> filePath)
    {
        added.Add(path);
        if (folder is null || !extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return new MediaImportResult(path, MediaImportOutcome.Unsupported, null, null);
        }

        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(path));
        bool renamed = false;
        for (int n = 2; File.Exists(target); n++)
        {
            if (File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(path)))
            {
                return new MediaImportResult(path, MediaImportOutcome.AlreadyPresent, MediaIds.User(Path.GetFileName(target)), null);
            }

            target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)} ({n}){Path.GetExtension(path)}");
            renamed = true;
        }

        File.Copy(path, target);
        string id = MediaIds.User(Path.GetFileName(target));
        items.Add(create(id, target));
        return new MediaImportResult(path, renamed ? MediaImportOutcome.Renamed : MediaImportOutcome.Added, id, null);
    }
}
