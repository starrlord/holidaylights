using System.Globalization;

namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>Creates a new bulb from a GIF (PRODUCT-SPEC 3.2.10, the 5.4 "Add Bulb..." with a <c>.gif</c>). Owner: bulb-factory.</summary>
/// <remarks>The defaults are those 5.4 used when it created a bulb from a GIF.</remarks>
public static class GifBulbFactory
{
    /// <summary>The description of a new bulb (5.4).</summary>
    public const string DefaultDescription = "No description is available for this bulb.";

    /// <summary>The author of a new bulb when the Windows account has no name (5.4).</summary>
    public const string NoAuthorText = "No author information is available for this bulb.";

    /// <summary>The copyright of a new bulb when the Windows account has no name (5.4).</summary>
    public const string NoCopyrightText = "No copyright information is available for this bulb.";

    /// <summary>The bulb file extension.</summary>
    public const string Extension = ".bul";

    /// <summary>The 5.4 numbering stops at "&lt;name&gt; 999.bul" (<c>File_CreateUnique</c>).</summary>
    private const int MaxNumber = 999;

    /// <summary>The name of a bulb (and its file) when the GIF's file name has no usable character.</summary>
    private const string FallbackName = "New Bulb";

    /// <summary>
    /// Builds the 5.4 default document: all 9 slots use the GIF; the list-preview square centred
    /// (<c>W &gt;= 32 ? W/2 - 16 : 0</c>, same for Y); description "No description is available for this bulb.";
    /// author = <paramref name="authorName"/>; copyright "Copyright &lt;year&gt; &lt;name&gt;".
    /// </summary>
    /// <remarks>Texts are cut to 79 characters. Without an author name the 5.4 texts "No author information..." and "No copyright information..." are used.</remarks>
    /// <param name="gif">The GIF bytes (must decode with the faithful decoder).</param>
    /// <param name="name">The bulb name: the GIF's file name without extension.</param>
    /// <param name="authorName">The Windows account display name.</param>
    /// <param name="year">The copyright year.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidDataException">The GIF cannot be decoded ("Cannot Import GIF File").</exception>
    public static BulbDocument Create(ReadOnlyMemory<byte> gif, string name, string authorName, int year)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(authorName);
        BulbGif art = BulbGif.FromBytes(gif.Span);

        // A .bul stores Windows-1252 (PRODUCT-SPEC 6.3): a name or author with no character left that a bulb file can
        // store would read "????????" or "?? ??", so it falls back to "New Bulb" or the 5.4 no-author texts (review r1 #69).
        string author = Storable(authorName.Trim());
        string bulbName = Storable(name.Trim());
        var document = new BulbDocument
        {
            Name = BulText.Truncate(bulbName.Length > 0 ? bulbName : FallbackName, BulText.MaxFieldLength),
            Description = DefaultDescription,
            Author = author.Length > 0 ? BulText.Truncate(author, BulText.MaxFieldLength) : NoAuthorText,
            Copyright = author.Length > 0
                ? BulText.Truncate(string.Create(CultureInfo.InvariantCulture, $"Copyright {year} {author}"), BulText.MaxFieldLength)
                : NoCopyrightText,
        };

        foreach (CellSlot slot in Enum.GetValues<CellSlot>())
        {
            document.SetAnimation(slot, 0, art);
        }

        return document;
    }

    /// <summary>The text as a bulb file stores it, or "" when no letter or digit would be left (every one would become '?').</summary>
    private static string Storable(string text)
    {
        if (BulText.CanStore(text))
        {
            return text;
        }

        string stored = Windows1252.Decode(BulText.Encode(text, out _));
        return stored.Any(char.IsLetterOrDigit) ? stored : "";
    }

    /// <summary>The 5.4 file naming: <c>&lt;name&gt;.bul</c>, else <c>&lt;name&gt; 1.bul</c>, <c>&lt;name&gt; 2.bul</c>, ... in the folder.</summary>
    /// <remarks>Characters that cannot appear in a file name become "_".</remarks>
    /// <param name="folder">My Bulbs.</param>
    /// <param name="name">The bulb name.</param>
    /// <returns>A path that does not exist yet.</returns>
    /// <exception cref="IOException">"&lt;name&gt;.bul" to "&lt;name&gt; 999.bul" all exist.</exception>
    public static string ChooseFileName(string folder, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(name);
        string stem = FileStem(name);
        for (int number = 0; number <= MaxNumber; number++)
        {
            string fileName = number == 0 ? stem : string.Create(CultureInfo.InvariantCulture, $"{stem} {number}");
            string path = Path.Combine(folder, fileName + Extension);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }
        }

        throw new IOException($"Every file name from \"{stem}{Extension}\" to \"{stem} {MaxNumber}{Extension}\" is taken.");
    }

    private static string FileStem(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string stem = string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)).Trim().TrimEnd('.');
        return stem.Length > 0 ? stem : FallbackName;
    }
}
