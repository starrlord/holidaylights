namespace HolidayLights.Core.Abstractions;

/// <summary>Where a bulb comes from (PRODUCT-SPEC 6.3).</summary>
public enum BulbOrigin
{
    /// <summary>One of the 49 bulbs built into Holiday Lights 5.4 (embedded table and art). Id <c>builtin:&lt;slug&gt;</c>.</summary>
    BuiltIn,

    /// <summary>One of the 1,501 add-on <c>.bul</c> files shipped in <c>Content\Bulbs</c> (read-only). Id <c>addon:&lt;file stem&gt;</c>.</summary>
    BundledAddOn,

    /// <summary>An add-on <c>.bul</c> file in the user's My Bulbs folder. Id <c>user:&lt;file stem&gt;</c>.</summary>
    UserAddOn,
}

/// <summary>
/// Stable string ids of bulbs, used in settings, themes, arrangements and caches (never 5.4's colliding numeric ids).
/// </summary>
/// <remarks>
/// Formats: <c>builtin:&lt;slug&gt;</c> (slug from <c>assets/builtin/table.json</c>, e.g. <c>builtin:standard-bulbs</c>),
/// <c>addon:&lt;file stem&gt;</c> (e.g. <c>addon:MulticolorBubbleLights</c>), <c>user:&lt;file stem&gt;</c>.
/// Ids are compared with <see cref="Comparer"/> (ordinal, case-insensitive), because file stems are case-insensitive on
/// Windows.
/// </remarks>
public static class BulbIds
{
    /// <summary>Prefix of built-in bulb ids.</summary>
    public const string BuiltInPrefix = "builtin:";

    /// <summary>Prefix of bundled add-on bulb ids.</summary>
    public const string AddOnPrefix = "addon:";

    /// <summary>Prefix of My Bulbs ids.</summary>
    public const string UserPrefix = "user:";

    /// <summary>The comparer for bulb ids (ordinal, case-insensitive).</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Builds a built-in bulb id.</summary>
    /// <param name="slug">The slug (e.g. <c>standard-bulbs</c>).</param>
    /// <returns><c>builtin:&lt;slug&gt;</c>.</returns>
    public static string BuiltIn(string slug) => BuiltInPrefix + slug;

    /// <summary>Builds a bundled add-on bulb id.</summary>
    /// <param name="fileStem">The <c>.bul</c> file name without extension.</param>
    /// <returns><c>addon:&lt;file stem&gt;</c>.</returns>
    public static string AddOn(string fileStem) => AddOnPrefix + fileStem;

    /// <summary>Builds a My Bulbs id.</summary>
    /// <param name="fileStem">The <c>.bul</c> file name without extension.</param>
    /// <returns><c>user:&lt;file stem&gt;</c>.</returns>
    public static string User(string fileStem) => UserPrefix + fileStem;

    /// <summary>Returns the origin encoded in an id.</summary>
    /// <param name="id">A bulb id.</param>
    /// <param name="origin">The origin when the id is well-formed.</param>
    /// <returns>True when <paramref name="id"/> has a known prefix and a non-empty key.</returns>
    public static bool TryGetOrigin(string? id, out BulbOrigin origin)
    {
        origin = default;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (HasPrefix(id, BuiltInPrefix))
        {
            origin = BulbOrigin.BuiltIn;
        }
        else if (HasPrefix(id, AddOnPrefix))
        {
            origin = BulbOrigin.BundledAddOn;
        }
        else if (HasPrefix(id, UserPrefix))
        {
            origin = BulbOrigin.UserAddOn;
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>True when the id has a known prefix and a non-empty key.</summary>
    /// <param name="id">A candidate id.</param>
    /// <returns>True for a well-formed bulb id.</returns>
    public static bool IsValid(string? id) => TryGetOrigin(id, out _);

    /// <summary>Returns the part after the prefix (slug or file stem).</summary>
    /// <param name="id">A well-formed bulb id.</param>
    /// <returns>The slug or file stem.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a bulb id.</exception>
    public static string GetKey(string id) =>
        TryGetOrigin(id, out _) ? id[(id.IndexOf(':') + 1)..] : throw new ArgumentException($"'{id}' is not a bulb id.", nameof(id));

    private static bool HasPrefix(string id, string prefix) =>
        id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
