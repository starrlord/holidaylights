using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>Turns a GIF into a new bulb file (PRODUCT-SPEC 3.2.10, the 5.4 "Add Bulb..." with a <c>.gif</c>).</summary>
internal interface IGifBulbWriter
{
    /// <summary>Creates the bulb file with the 5.4 defaults.</summary>
    /// <param name="gifPath">The GIF.</param>
    /// <param name="folder">My Bulbs (it exists).</param>
    /// <param name="authorName">The author and copyright holder.</param>
    /// <param name="year">The copyright year.</param>
    /// <param name="bulbId">A header bulb id no loaded bulb uses.</param>
    /// <returns>The path of the new file.</returns>
    /// <exception cref="InvalidDataException">The GIF cannot be decoded.</exception>
    /// <exception cref="IOException">The GIF cannot be read or the bulb cannot be written.</exception>
    string Write(string gifPath, string folder, string authorName, int year, int bulbId);
}

/// <summary>The Bulb Factory writer (<see cref="GifBulbFactory"/>, <see cref="BulFileWriter"/>) behind <see cref="IGifBulbWriter"/>.</summary>
internal sealed class BulbFactoryGifWriter : IGifBulbWriter
{
    /// <inheritdoc />
    public string Write(string gifPath, string folder, string authorName, int year, int bulbId)
    {
        byte[] gif = File.ReadAllBytes(gifPath);
        string name = Path.GetFileNameWithoutExtension(gifPath);
        BulbDocument document = GifBulbFactory.Create(gif, name, authorName, year);
        string path = GifBulbFactory.ChooseFileName(folder, name);
        BulFileWriter.WriteAtomically(path, BulFileWriter.Encode(document, bulbId));
        return path;
    }
}
