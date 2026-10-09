namespace HolidayLights.Core.Settings;

/// <summary>
/// Validation rules shared by the settings sanitizer, the theme files and the 5.4 import. Every method returns its input
/// unchanged (the same instance) when it is already valid, so sanitizing valid settings allocates nothing.
/// </summary>
internal static class ValueRules
{
    /// <summary>The longest screen saver message (5.4 edit control 305).</summary>
    public const int MaxMessageLength = 255;

    /// <summary>The smallest message font size in points (5.4 ChooseFont limit).</summary>
    public const int MinFontSize = 18;

    /// <summary>The largest message font size in points (5.4 ChooseFont limit).</summary>
    public const int MaxFontSize = 100;

    /// <summary>The 5.4 default font face, also used when a font has no family.</summary>
    public const string DefaultFontFamily = "Arial";

    /// <summary>True for an interval the flash clock accepts (1-9).</summary>
    /// <param name="interval">A flash interval.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValidInterval(int interval) => interval is >= FlashSettings.MinInterval and <= FlashSettings.MaxInterval;

    /// <summary>An interval outside 1-9 becomes 5 (PRODUCT-SPEC 6.8.2).</summary>
    /// <param name="interval">A flash interval.</param>
    /// <returns>The interval, or the default.</returns>
    public static int NormalizeInterval(int interval) => IsValidInterval(interval) ? interval : FlashSettings.DefaultInterval;

    /// <summary>Removes empty and malformed ids from an edge and keeps at most six (repeats are allowed).</summary>
    /// <param name="ids">The edge, possibly null when read from a file.</param>
    /// <returns>The clean edge.</returns>
    public static IReadOnlyList<string> CleanEdge(IReadOnlyList<string>? ids)
    {
        if (ids is not null && ids.Count <= SlotAssignment.MaxTypesPerEdge && ids.All(BulbIds.IsValid))
        {
            return ids;
        }

        return ids is null ? [] : [.. ids.Where(BulbIds.IsValid).Take(SlotAssignment.MaxTypesPerEdge)];
    }

    /// <summary>A malformed corner id becomes an empty corner.</summary>
    /// <param name="id">The corner id.</param>
    /// <returns>The id or null.</returns>
    public static string? CleanCorner(string? id) => BulbIds.IsValid(id) ? id : null;

    /// <summary>Cleans every box of an arrangement.</summary>
    /// <param name="arrangement">The arrangement.</param>
    /// <returns>The same instance when every box is valid, else a cleaned copy.</returns>
    public static SlotAssignment CleanArrangement(SlotAssignment arrangement)
    {
        IReadOnlyList<string> top = CleanEdge(arrangement.Top);
        IReadOnlyList<string> right = CleanEdge(arrangement.Right);
        IReadOnlyList<string> bottom = CleanEdge(arrangement.Bottom);
        IReadOnlyList<string> left = CleanEdge(arrangement.Left);
        string? topLeft = CleanCorner(arrangement.TopLeft);
        string? topRight = CleanCorner(arrangement.TopRight);
        string? bottomLeft = CleanCorner(arrangement.BottomLeft);
        string? bottomRight = CleanCorner(arrangement.BottomRight);
        bool unchanged = ReferenceEquals(top, arrangement.Top) && ReferenceEquals(right, arrangement.Right)
            && ReferenceEquals(bottom, arrangement.Bottom) && ReferenceEquals(left, arrangement.Left)
            && topLeft == arrangement.TopLeft && topRight == arrangement.TopRight
            && bottomLeft == arrangement.BottomLeft && bottomRight == arrangement.BottomRight;
        return unchanged
            ? arrangement
            : new SlotAssignment
            {
                Top = top, Right = right, Bottom = bottom, Left = left,
                TopLeft = topLeft, TopRight = topRight, BottomLeft = bottomLeft, BottomRight = bottomRight,
            };
    }

    /// <summary>Removes null, invalid and duplicate entries from a list of ids.</summary>
    /// <param name="ids">The ids, possibly null when read from a file.</param>
    /// <param name="isValid">Tells whether an id is well-formed.</param>
    /// <param name="comparer">The id comparer (duplicates are compared with it).</param>
    /// <returns>The same list when already clean, else a cleaned copy in the original order.</returns>
    public static IReadOnlyList<string> CleanIdSet(IReadOnlyList<string>? ids, Func<string?, bool> isValid, StringComparer comparer)
    {
        if (ids is null)
        {
            return [];
        }

        var seen = new HashSet<string>(comparer);
        bool clean = true;
        foreach (string id in ids)
        {
            clean &= isValid(id) && seen.Add(id);
        }

        if (clean)
        {
            return ids;
        }

        seen.Clear();
        return [.. ids.Where(id => isValid(id) && seen.Add(id))];
    }

    /// <summary>True for a well-formed song or picture id.</summary>
    /// <param name="id">A media id.</param>
    /// <returns>True when valid.</returns>
    public static bool IsMediaId(string? id) => MediaIds.TryParse(id, out _, out _);

    /// <summary>
    /// The canonical spelling of a screen saver animation: one of the 25 built-in names (matched case-insensitively), or a
    /// bulb animation with a well-formed bulb id.
    /// </summary>
    /// <param name="animation">The stored value.</param>
    /// <returns>The canonical value, or null when the animation is unknown.</returns>
    public static string? CanonicalAnimation(string? animation)
    {
        if (string.IsNullOrEmpty(animation))
        {
            return null;
        }

        if (SaverAnimations.TryGetBulbId(animation, out string? bulbId))
        {
            return BulbIds.IsValid(bulbId) ? animation : null;
        }

        return SaverAnimations.All.FirstOrDefault(a => string.Equals(a, animation, StringComparison.Ordinal))
            ?? SaverAnimations.All.FirstOrDefault(a => string.Equals(a, animation, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes leading spaces and blank lines (characters up to U+0020, as 5.4 did while typing) and limits the length to 255.</summary>
    /// <param name="message">The stored message.</param>
    /// <returns>The clean message.</returns>
    public static string CleanMessage(string? message)
    {
        if (message is null)
        {
            return "";
        }

        int start = 0;
        while (start < message.Length && message[start] <= ' ')
        {
            start++;
        }

        int length = Math.Min(message.Length - start, MaxMessageLength);
        return start == 0 && length == message.Length ? message : message.Substring(start, length);
    }

    /// <summary>Gives a font a family (Arial when empty) and a size of 18-100 points.</summary>
    /// <param name="font">The stored font, possibly null.</param>
    /// <returns>The same instance when valid, else a corrected copy.</returns>
    public static SaverFont CleanFont(SaverFont? font)
    {
        if (font is null)
        {
            return new SaverFont();
        }

        string family = string.IsNullOrWhiteSpace(font.Family) ? DefaultFontFamily : font.Family.Trim();
        int size = Math.Clamp(font.SizePt, MinFontSize, MaxFontSize);
        return family == font.Family && size == font.SizePt ? font : font with { Family = family, SizePt = size };
    }

    /// <summary>True for "(None)" and well-formed picture ids.</summary>
    /// <param name="picture">The stored value.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValidPicture(string? picture) => picture == SaverPictures.None || IsMediaId(picture);
}
