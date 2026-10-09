namespace HolidayLights.Core.Abstractions;

/// <summary>Where a song or picture comes from.</summary>
public enum MediaOrigin
{
    /// <summary>Shipped with Holiday Lights (<c>Content\Music</c>, <c>Content\Pictures</c>; read-only).</summary>
    Bundled,

    /// <summary>The user's My Music or My Pictures folder (<c>Documents\Holiday Lights\...</c>).</summary>
    User,
}

/// <summary>
/// Stable ids of songs and pictures: <c>bundled:&lt;file name&gt;</c> and <c>user:&lt;file name&gt;</c> (file name with
/// extension, e.g. <c>bundled:Jingle Bells (Reggae).mid</c>, <c>bundled:Santa Candle.BMP</c>, <c>user:Song.lnk</c>).
/// Compared with <see cref="Comparer"/> (ordinal, case-insensitive).
/// </summary>
public static class MediaIds
{
    /// <summary>Prefix of bundled songs and pictures.</summary>
    public const string BundledPrefix = "bundled:";

    /// <summary>Prefix of the user's songs and pictures.</summary>
    public const string UserPrefix = "user:";

    /// <summary>The comparer for media ids.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Builds a bundled id.</summary>
    /// <param name="fileName">File name with extension.</param>
    /// <returns><c>bundled:&lt;file name&gt;</c>.</returns>
    public static string Bundled(string fileName) => BundledPrefix + fileName;

    /// <summary>Builds a user id.</summary>
    /// <param name="fileName">File name with extension.</param>
    /// <returns><c>user:&lt;file name&gt;</c>.</returns>
    public static string User(string fileName) => UserPrefix + fileName;

    /// <summary>Splits an id into origin and file name.</summary>
    /// <param name="id">A media id.</param>
    /// <param name="origin">The origin.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>True when the id is well-formed.</returns>
    public static bool TryParse(string? id, out MediaOrigin origin, out string fileName)
    {
        origin = default;
        fileName = "";
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (id.Length > BundledPrefix.Length && id.StartsWith(BundledPrefix, StringComparison.OrdinalIgnoreCase))
        {
            origin = MediaOrigin.Bundled;
            fileName = id[BundledPrefix.Length..];
            return true;
        }

        if (id.Length > UserPrefix.Length && id.StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase))
        {
            origin = MediaOrigin.User;
            fileName = id[UserPrefix.Length..];
            return true;
        }

        return false;
    }
}

/// <summary>Outcome of adding a song or picture file.</summary>
public enum MediaImportOutcome
{
    /// <summary>Copied into the user folder under its own name.</summary>
    Added,

    /// <summary>Copied under a new name ("&lt;name&gt; (2)") because a different file had the same name.</summary>
    Renamed,

    /// <summary>The same content is already present; nothing copied.</summary>
    AlreadyPresent,

    /// <summary>Not a supported file type.</summary>
    Unsupported,

    /// <summary>The copy failed (see <see cref="MediaImportResult.Error"/>).</summary>
    Failed,
}

/// <summary>Result of adding one song or picture file.</summary>
/// <param name="SourcePath">The file that was added.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Id">The new or existing item; null when unsupported or failed.</param>
/// <param name="Error">Windows reason for <see cref="MediaImportOutcome.Failed"/>.</param>
public sealed record MediaImportResult(string SourcePath, MediaImportOutcome Outcome, string? Id, string? Error);
