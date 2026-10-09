namespace HolidayLights.Core.Legacy;

/// <summary>Builds the resolver of an import from the analysis and the (predicted or actual) file outcomes.</summary>
internal static class LegacyResolvers
{
    /// <summary>Creates the resolver.</summary>
    /// <param name="analysis">The analysis.</param>
    /// <param name="files">Where the 5.4 files are in 6.0.</param>
    /// <param name="songs">Bundled songs named by 5.4 lists but absent from its folder.</param>
    /// <param name="pictures">Bundled pictures named by 5.4 but absent from its folder.</param>
    /// <returns>The resolver.</returns>
    public static LegacyResolver Create(LegacyAnalysis analysis, LegacyFileImport files, ISongLibrary songs, IPictureLibrary pictures)
    {
        Dictionary<int, LegacyAddOn> addOnsById = analysis.AddOns.Loaded.ToDictionary(a => a.RuntimeId);
        return new LegacyResolver(
            id => LegacyBuiltInBulbs.ById(id) ?? (addOnsById.TryGetValue(id, out LegacyAddOn? addOn) ? files.AddOnId(addOn) : null),
            name => analysis.AddOns.Loaded.Where(a => string.Equals(a.Name, name, StringComparison.Ordinal)).Select(files.AddOnId).FirstOrDefault(id => id is not null),
            name => FindFile(analysis.Songs, name) is { } file ? files.SongId(file) : BundledSong(songs, name),
            name => FindFile(analysis.Pictures, name) is { } file ? files.PictureId(file) : BundledPicture(pictures, name));
    }

    private static LegacyMediaFile? FindFile(IReadOnlyList<LegacyMediaFile> files, string name) =>
        files.FirstOrDefault(f => string.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase));

    private static string? BundledSong(ISongLibrary songs, string name) =>
        songs.TryGetSong(MediaIds.Bundled(name), out SongInfo? song) && song.Origin == MediaOrigin.Bundled ? song.Id : null;

    private static string? BundledPicture(IPictureLibrary pictures, string name) =>
        pictures.TryGetPicture(MediaIds.Bundled(name), out PictureInfo? picture) && picture.Origin == MediaOrigin.Bundled ? picture.Id : null;
}
