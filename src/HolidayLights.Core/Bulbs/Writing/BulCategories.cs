namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// Bulb category names as Holiday Lights 5.4 creates, stores and checks them: the <c>categ:</c> record holds the names joined with "|"; 5.4 rejects a file whose
/// record is longer than 255 characters or holds a name longer than 39 characters, and stops reading at an empty name.
/// </summary>
public static class BulCategories
{
    /// <summary>Longest category name ("New Category" limits its text box to 39 characters).</summary>
    public const int MaxNameLength = 39;

    /// <summary>Longest <c>categ:</c> text that 5.4 can load (5.4 copies it into a 256-byte buffer).</summary>
    public const int MaxRecordLength = 255;

    private const char Separator = '|';

    /// <summary>"New Category": OK is enabled when the name contains a letter or a digit (5.4 <c>_isctype(c, 0x107)</c>).</summary>
    /// <param name="name">The typed name.</param>
    /// <returns>True when the name can become a category.</returns>
    public static bool IsUsableName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Any(char.IsLetterOrDigit);
    }

    /// <summary>
    /// The name a typed category becomes (5.4 <c>NewCategoryDlgProc</c>): every "|" becomes a space, leading and trailing
    /// spaces are removed, and it is cut to <see cref="MaxNameLength"/> characters.
    /// </summary>
    /// <param name="name">The typed name.</param>
    /// <returns>The category name (empty when nothing is left).</returns>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return BulText.Truncate(name.Replace(Separator, ' ').Trim(), MaxNameLength).TrimEnd();
    }

    /// <summary>
    /// The text of the <c>categ:</c> record: the names normalized, without empty, internal (<c>_0</c>, <c>_1</c>) and
    /// repeated (ignoring case) names, joined with "|" in the given order. Names that would make the text longer than
    /// <see cref="MaxRecordLength"/> characters are left out, so 5.4 can always load the file.
    /// </summary>
    /// <param name="names">The checked categories.</param>
    /// <param name="omitted">The names left out because the record was full.</param>
    /// <returns>The record text (without the <c>categ:</c> prefix and the NUL).</returns>
    public static string Join(IEnumerable<string> names, out IReadOnlyList<string> omitted)
    {
        ArgumentNullException.ThrowIfNull(names);
        var kept = new List<string>();
        var left = new List<string>();
        int length = 0;
        foreach (string name in Distinct(names))
        {
            int added = (kept.Count == 0 ? 0 : 1) + BulText.Encode(name, out _).Length;
            if (length + added > MaxRecordLength)
            {
                left.Add(name);
                continue;
            }

            kept.Add(name);
            length += added;
        }

        omitted = left;
        return string.Join(Separator, kept);
    }

    /// <summary>True when every one of the names fits in a bulb file (see <see cref="Join"/>).</summary>
    /// <param name="names">The checked categories.</param>
    /// <returns>False when <see cref="Join"/> would leave some out.</returns>
    public static bool FitInFile(IEnumerable<string> names)
    {
        Join(names, out IReadOnlyList<string> omitted);
        return omitted.Count == 0;
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in names)
        {
            string name = Normalize(raw);
            if (name.Length > 0 && name is not ("_0" or "_1") && seen.Add(name))
            {
                yield return name;
            }
        }
    }
}
