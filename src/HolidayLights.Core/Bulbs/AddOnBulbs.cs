namespace HolidayLights.Core.Bulbs;

/// <summary>Creates <see cref="IBulb"/> instances for <c>.bul</c> files. Owner: core-bulbs.</summary>
public static class AddOnBulbs
{
    /// <summary>
    /// Wraps a parsed file as a bulb (the faithful 5.4 decoder, light-bulb classification of PRODUCT-SPEC 5.4, WARNING
    /// placeholder for undecodable entries). Bulb Editing uses it for its unsaved document with a unique content key.
    /// </summary>
    /// <remarks>
    /// Animation facts are worked out by decoding each GIF the first time they are needed; decoded frames are shared
    /// through a process-wide cache keyed by <paramref name="contentKey"/>, so every revision needs its own key.
    /// </remarks>
    /// <param name="file">A parsed, undamaged file.</param>
    /// <param name="id">The bulb id (<c>addon:</c> or <c>user:</c> + file stem).</param>
    /// <param name="origin">Bundled or My Bulbs.</param>
    /// <param name="contentKey">The cache key of its art (<see cref="IBulb.ContentKey"/>).</param>
    /// <returns>The bulb.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is damaged.</exception>
    public static IBulb FromFile(BulFile file, string id, BulbOrigin origin, string contentKey)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(contentKey);
        return new AddOnBulb(file, id, origin, contentKey, knownFacts: null, DecodedArtCache.Shared);
    }
}
