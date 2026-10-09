using HolidayLights.Core.Bulbs;

namespace HolidayLights.Core.Legacy;

/// <summary>What the 5.4 import needs from a <c>.bul</c> header.</summary>
/// <param name="LegacyId">The stored bulb id (header 0x0C).</param>
/// <param name="Name">The bulb name (header 0x1C), used to match a 5.4 screen saver animation.</param>
/// <param name="Categories">The categories of the file's <c>categ:</c> record (what 5.4's Edit Categories saved), or null when unknown.</param>
internal sealed record LegacyBulbHeader(int LegacyId, string Name, IReadOnlyList<string>? Categories = null);

/// <summary>Reads <c>.bul</c> headers for the 5.4 import.</summary>
internal interface ILegacyBulbHeaderReader
{
    /// <summary>Reads a file's header.</summary>
    /// <param name="path">The <c>.bul</c> file.</param>
    /// <returns>The header, or null when 5.4 would not have loaded the file (damaged or unreadable).</returns>
    LegacyBulbHeader? Read(string path);
}

/// <summary>Reads headers with the shared <c>.bul</c> parser and its 5.4 loader rules.</summary>
internal sealed class BulFileHeaderReader : ILegacyBulbHeaderReader
{
    /// <inheritdoc />
    public LegacyBulbHeader? Read(string path)
    {
        try
        {
            BulFile file = BulFile.Read(path);
            return file.IsDamaged ? null : new LegacyBulbHeader(file.LegacyId, file.Name, file.Categories);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
