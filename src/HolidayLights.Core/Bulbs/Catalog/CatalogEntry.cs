namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// Everything the catalog knows about one listed or hidden bulb without decoding pixels: its metadata, the facts the
/// Bulb List shows and, for add-ons, the facts of every animation the slot table uses (so bulbs are created without
/// decoding again).
/// </summary>
internal sealed record CatalogEntry
{
    /// <summary>The bulb id.</summary>
    public required string Id { get; init; }

    /// <summary>Where it comes from.</summary>
    public required BulbOrigin Origin { get; init; }

    /// <summary>Name, verbatim.</summary>
    public required string Name { get; init; }

    /// <summary>Description as shown (the 5.4 damaged-bulb text when some art is damaged).</summary>
    public string Description { get; init; } = "";

    /// <summary>Author, verbatim.</summary>
    public string Author { get; init; } = "";

    /// <summary>Copyright, verbatim.</summary>
    public string Copyright { get; init; } = "";

    /// <summary>The bulb's own categories (internal <c>_0</c>/<c>_1</c> removed).</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The 5.4 numeric id.</summary>
    public int LegacyId { get; init; }

    /// <summary>Header <c>locked</c> (always true for built-ins).</summary>
    public bool Locked { get; init; } = true;

    /// <summary>Position in the built-in table, or -1 for add-ons.</summary>
    public int TableIndex { get; init; } = -1;

    /// <summary>Full path of the file; null for built-ins.</summary>
    public string? FilePath { get; init; }

    /// <summary>File size in bytes (0 for built-ins).</summary>
    public long FileSize { get; init; }

    /// <summary>Last write time (UTC); null for built-ins.</summary>
    public DateTime? LastWriteUtc { get; init; }

    /// <summary>Creation time (UTC), the time the file arrived in its folder; null for built-ins.</summary>
    public DateTime? CreatedUtc { get; init; }

    /// <summary>The <c>.bul</c> content identity (PRODUCT-SPEC 6.3); null for built-ins.</summary>
    public string? Identity { get; init; }

    /// <summary>Flavors on the top edge.</summary>
    public int TopFlavorCount { get; init; } = 1;

    /// <summary>Kind of the top edge, flavor 1.</summary>
    public BulbAnimationKind TopKind { get; init; }

    /// <summary>The largest top-edge cell (by area).</summary>
    public SizeI LargestTopCell { get; init; }

    /// <summary>True when any cell is larger than 64 px in either direction.</summary>
    public bool IsBig { get; init; }

    /// <summary>True when some animation is undecodable.</summary>
    public bool HasDamagedArt { get; init; }

    /// <summary>Facts per entry index of every animation the slot table uses (add-ons only).</summary>
    public IReadOnlyDictionary<int, AnimationFacts> Facts { get; init; } = new Dictionary<int, AnimationFacts>();

    /// <summary>What the header of an add-on file says (null for built-ins).</summary>
    public AddOnHeader? Header { get; init; }

    /// <summary>The folded name, words and other searchable text.</summary>
    public required SearchText Search { get; init; }

    /// <summary>The <see cref="IBulb.ContentKey"/> of the bulb this entry describes.</summary>
    public string ContentKey => Origin == BulbOrigin.BuiltIn ? Id : ContentKeyFor(Id, FileSize, LastWriteUtc ?? default);

    /// <summary>True for a My Bulbs file the user may edit in Bulb Editing.</summary>
    public bool IsEditable => Origin == BulbOrigin.UserAddOn && !Locked;

    /// <summary>The content key of a bulb file: the id plus the file's size and last write time.</summary>
    /// <param name="id">The bulb id.</param>
    /// <param name="size">The file size.</param>
    /// <param name="lastWriteUtc">The last write time.</param>
    /// <returns>The key.</returns>
    public static string ContentKeyFor(string id, long size, DateTime lastWriteUtc) => $"{id}|{size}|{lastWriteUtc.Ticks}";

    /// <summary>Describes a built-in bulb.</summary>
    /// <param name="bulb">The bulb.</param>
    /// <returns>The entry.</returns>
    public static CatalogEntry FromBuiltIn(BuiltInBulb bulb)
    {
        var topCells = Enumerable.Range(0, bulb.GetFlavorCount(Side.Top)).Select(f => bulb.GetCellSize(CellSlot.Top, f, 0)).ToList();
        return new CatalogEntry
        {
            Id = bulb.Id,
            Origin = BulbOrigin.BuiltIn,
            Name = bulb.Name,
            Description = bulb.Description,
            Author = bulb.Author,
            Copyright = bulb.Copyright,
            Categories = bulb.Categories,
            LegacyId = bulb.LegacyId,
            TableIndex = bulb.TableIndex,
            TopFlavorCount = topCells.Count,
            TopKind = bulb.GetAnimation(CellSlot.Top, 0).Kind,
            LargestTopCell = Largest(topCells),
            IsBig = AllCellSizes(bulb).Any(IsBigCell),
            Search = SearchText.For(bulb.Name, bulb.Description, bulb.Author, fileName: ""),
        };
    }

    /// <summary>Describes an add-on file from its header and the facts of its animations.</summary>
    /// <param name="header">The header summary.</param>
    /// <param name="id">The bulb id.</param>
    /// <param name="origin">Bundled or My Bulbs.</param>
    /// <param name="stamp">Size and times of the file.</param>
    /// <param name="facts">Facts of every in-range entry the slot table uses.</param>
    /// <returns>The entry.</returns>
    public static CatalogEntry FromAddOn(AddOnHeader header, string id, BulbOrigin origin, FileStamp stamp, IReadOnlyDictionary<int, AnimationFacts> facts)
    {
        AnimationFacts FactsOf(int entry) => facts.TryGetValue(entry, out AnimationFacts? f) ? f : AnimationFacts.Damaged;
        var top = header.TopEntries.Select(FactsOf).ToList();
        bool damagedArt = header.ReferencedEntries.Any(e => FactsOf(e).IsDamaged);

        // Shift-JIS / CP949 credits read as their authors wrote them (PO decision 4); the header keeps the file's text.
        (string author, string copyright) = CreditText.Decode(header.Author, header.Copyright);
        return new CatalogEntry
        {
            Id = id,
            Origin = origin,
            Name = header.Name,
            Description = damagedArt ? AddOnBulb.DamagedDescription : header.Description,
            Author = author,
            Copyright = copyright,
            Categories = [.. header.Categories.Where(c => c is not ("_0" or "_1"))],
            LegacyId = header.LegacyId,
            Locked = header.Locked,
            FilePath = stamp.Path,
            FileSize = stamp.Size,
            LastWriteUtc = stamp.LastWriteUtc,
            CreatedUtc = stamp.CreatedUtc,
            Identity = header.Identity,
            TopFlavorCount = top.Count,
            TopKind = top.Count > 0 ? top[0].Kind : BulbAnimationKind.Static,
            LargestTopCell = Largest(top.Select(f => f.Size)),
            IsBig = header.ReferencedEntries.Select(FactsOf).Any(f => IsBigCell(f.Size)),
            HasDamagedArt = damagedArt,
            Facts = facts,
            Header = header,
            Search = SearchText.For(header.Name, header.Description, author, Path.GetFileNameWithoutExtension(stamp.Path)),
        };
    }

    /// <summary>Decodes every animation the slot table of a file uses and describes the file.</summary>
    /// <param name="file">The parsed, undamaged file.</param>
    /// <param name="id">The bulb id.</param>
    /// <param name="origin">Bundled or My Bulbs.</param>
    /// <param name="stamp">Size and times of the file.</param>
    /// <returns>The entry.</returns>
    public static CatalogEntry Analyze(BulFile file, string id, BulbOrigin origin, FileStamp stamp)
    {
        AddOnHeader header = AddOnHeader.Of(file);
        var facts = new Dictionary<int, AnimationFacts>();
        foreach (int entry in header.ReferencedEntries.Distinct())
        {
            if (entry is >= 0 and < BulFile.EntryCount)
            {
                facts[entry] = AnimationFacts.Analyze(file.Entries[entry].Gif.Span);
            }
        }

        return FromAddOn(header, id, origin, stamp, facts);
    }

    private static IEnumerable<SizeI> AllCellSizes(BuiltInBulb bulb)
    {
        int phases = bulb.GetPhaseCount(Side.Top);
        foreach (Side side in CellSlots.Sides)
        {
            for (int flavor = 0; flavor < bulb.GetFlavorCount(side); flavor++)
            {
                for (int phase = 0; phase < phases; phase++)
                {
                    yield return bulb.GetCellSize(side.ToSlot(), flavor, phase);
                }
            }
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            for (int phase = 0; phase < phases; phase++)
            {
                yield return bulb.GetCellSize(corner.ToSlot(), 0, phase);
            }
        }

        yield return bulb.GetCellSize(CellSlot.Preview, 0, 0);
    }

    private static bool IsBigCell(SizeI size) => size.Width > 64 || size.Height > 64;

    private static SizeI Largest(IEnumerable<SizeI> sizes) =>
        sizes.Aggregate(default(SizeI), (best, s) => (long)s.Width * s.Height > (long)best.Width * best.Height ? s : best);
}

/// <summary>Where a bulb file is and when it was written.</summary>
/// <param name="Path">Full path.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="LastWriteUtc">Last write time (UTC).</param>
/// <param name="CreatedUtc">Creation time (UTC).</param>
internal sealed record FileStamp(string Path, long Size, DateTime LastWriteUtc, DateTime CreatedUtc)
{
    /// <summary>Reads the stamp of a file.</summary>
    /// <param name="info">The file.</param>
    /// <returns>The stamp.</returns>
    public static FileStamp Of(FileInfo info) => new(info.FullName, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
}

/// <summary>What the catalog keeps from the header of an add-on file.</summary>
/// <param name="LegacyId">Header bulb id.</param>
/// <param name="Locked">Header <c>locked</c>.</param>
/// <param name="Name">Name.</param>
/// <param name="Description">Description as stored.</param>
/// <param name="Author">Author.</param>
/// <param name="Copyright">Copyright.</param>
/// <param name="Categories">Categories of the <c>categ:</c> record.</param>
/// <param name="Identity">Content identity.</param>
/// <param name="TopEntries">Entry index of each top-edge flavor.</param>
/// <param name="ReferencedEntries">Every entry index the slot table uses.</param>
internal sealed record AddOnHeader(
    int LegacyId,
    bool Locked,
    string Name,
    string Description,
    string Author,
    string Copyright,
    IReadOnlyList<string> Categories,
    string Identity,
    IReadOnlyList<int> TopEntries,
    IReadOnlyList<int> ReferencedEntries)
{
    /// <summary>Summarizes a parsed file.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The summary.</returns>
    public static AddOnHeader Of(BulFile file) => new(
        file.LegacyId,
        file.Locked,
        file.Name,
        file.Description,
        file.Author,
        file.Copyright,
        file.Categories,
        file.ComputeContentIdentity(),
        [.. Enumerable.Range(0, file.GetFlavorCount(Side.Top)).Select(f => file.SideEntries[0][f])],
        [.. AddOnBulb.ReferencedEntries(file)]);
}
