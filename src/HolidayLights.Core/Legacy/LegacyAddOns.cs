namespace HolidayLights.Core.Legacy;

/// <summary>A 5.4 add-on bulb file as 5.4 loaded it.</summary>
/// <param name="FilePath">The file in <c>Holiday Lights Bulbs</c>.</param>
/// <param name="RuntimeId">The id 5.4 gave it at load time (the one "Bulb Settings" holds).</param>
/// <param name="Name">The bulb name (a "Screen Saver Module" value may name it).</param>
/// <param name="Categories">The categories 5.4 showed for it (the file's <c>categ:</c> record), or null when unknown.</param>
internal sealed record LegacyAddOn(string FilePath, int RuntimeId, string Name, IReadOnlyList<string>? Categories = null);

/// <summary>The add-on bulb files of a 5.4 installation.</summary>
/// <param name="Loaded">The files 5.4 loaded, in its load order, with their run-time ids.</param>
/// <param name="Damaged">The files 5.4 would have rejected (5.4 loader rules).</param>
internal sealed record LegacyAddOnFiles(IReadOnlyList<LegacyAddOn> Loaded, IReadOnlyList<string> Damaged)
{
    /// <summary>
    /// Re-creates the ids exactly as 5.4 assigned them (PRODUCT-SPEC 6.8.2): the <c>*.bul</c> files of
    /// <c>Holiday Lights Bulbs</c> in NTFS name order, each with its header id, incremented while it is -1 or taken (the
    /// built-in bulbs hold 0-48). Damaged files take no id, as 5.4 did not load them.
    /// </summary>
    /// <param name="programFolder">The 5.4 program folder, or null.</param>
    /// <param name="headers">Reads the headers.</param>
    /// <returns>The files.</returns>
    public static LegacyAddOnFiles Scan(string? programFolder, ILegacyBulbHeaderReader headers)
    {
        var loaded = new List<LegacyAddOn>();
        var damaged = new List<string>();
        var taken = new HashSet<int>();
        foreach (string path in LegacyFolders.ListFiles(programFolder, LegacyFolders.Bulbs, "*.bul"))
        {
            if (headers.Read(path) is not { } header)
            {
                damaged.Add(path);
                continue;
            }

            int id = header.LegacyId;
            while (id == -1 || LegacyBuiltInBulbs.IsBuiltInId(id) || taken.Contains(id))
            {
                id = unchecked(id + 1);
            }

            taken.Add(id);
            loaded.Add(new LegacyAddOn(path, id, header.Name, header.Categories));
        }

        return new LegacyAddOnFiles(loaded, damaged);
    }
}
