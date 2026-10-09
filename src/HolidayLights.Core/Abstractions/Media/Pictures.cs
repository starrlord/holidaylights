namespace HolidayLights.Core.Abstractions;

/// <summary>One screen saver background picture.</summary>
/// <param name="Id">Stable id (<see cref="MediaIds"/>), e.g. <c>bundled:Santa Candle.BMP</c>.</param>
/// <param name="Title">The tile caption: the file name without extension ("Santa Candle").</param>
/// <param name="FilePath">Full path of the file.</param>
/// <param name="Origin">Bundled or My Pictures.</param>
public sealed record PictureInfo(string Id, string Title, string FilePath, MediaOrigin Origin);

/// <summary>
/// Screen saver pictures: the 11 bundled pictures (<c>Content\Pictures</c>) and My Pictures (watched), with hidden bundled
/// pictures (settings <c>pictures.hidden</c>). Implemented by screensaver.
/// </summary>
/// <remarks>Mutating members update files and settings and must be called on the UI thread. <see cref="Changed"/> may be raised on any thread.</remarks>
public interface IPictureLibrary
{
    /// <summary>Listed pictures: bundled A-Z, then My Pictures A-Z ("(None)" is not an item).</summary>
    IReadOnlyList<PictureInfo> Pictures { get; }

    /// <summary>Raised when the folder changed or pictures were added, removed, hidden or restored.</summary>
    event EventHandler? Changed;

    /// <summary>Scans both folders and starts watching My Pictures.</summary>
    void Start();

    /// <summary>Finds a picture, hidden ones included.</summary>
    /// <param name="id">A picture id.</param>
    /// <param name="picture">The picture when found.</param>
    /// <returns>True when known and the file exists.</returns>
    bool TryGetPicture(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PictureInfo? picture);

    /// <summary>Decodes a picture at its own size (bundled pictures: the second embedded BMP; BMP, GIF first frame, JPEG, PNG).</summary>
    /// <param name="id">A picture id.</param>
    /// <returns>The picture, or null when missing or undecodable (the saver then runs without a picture, as 5.4).</returns>
    Rgba32Image? LoadImage(string id);

    /// <summary>Copies picture files (<c>.bmp .jpg .jpeg .gif .png</c>) into My Pictures.</summary>
    /// <param name="paths">The files.</param>
    /// <returns>One result per file, in order.</returns>
    IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths);

    /// <summary>"Remove Picture": a user picture moves to the holding folder; a bundled picture is hidden.</summary>
    /// <param name="id">The picture id.</param>
    /// <returns>The held file for user pictures (for undo); null for bundled pictures.</returns>
    HeldItem? Remove(string id);

    /// <summary>Brings back a removed user picture from the holding folder.</summary>
    /// <param name="item">The held file returned by <see cref="Remove"/>.</param>
    void Restore(HeldItem item);

    /// <summary>"Restore Removed Pictures": lists every hidden bundled picture again.</summary>
    void RestoreHiddenPictures();
}
