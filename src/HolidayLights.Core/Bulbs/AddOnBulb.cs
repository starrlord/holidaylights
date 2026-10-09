using HolidayLights.Core.Imaging;

namespace HolidayLights.Core.Bulbs;

/// <summary>
/// An add-on bulb: a parsed, undamaged <c>.bul</c> file drawn with the faithful 5.4 decoder.
/// Undecodable animations are drawn as the 5.4 WARNING picture.
/// </summary>
/// <remarks>
/// Thread-safe and immutable. The facts of an animation (size, frame count, kind) come from the catalog's index when it
/// has them; otherwise they are worked out by decoding the GIF once. Decoded frames live in a shared LRU cache.
/// </remarks>
internal sealed class AddOnBulb : IBulb
{
    /// <summary>The 5.4 description of a bulb with damaged art (verbatim).</summary>
    public const string DamagedDescription = "This bulb is damaged and may cause problems.";

    private readonly BulFile file;
    private readonly DecodedArtCache cache;
    private readonly object gate = new();
    private readonly AnimationFacts?[] facts = new AnimationFacts?[BulFile.EntryCount];
    private readonly Lazy<bool> hasDamagedArt;

    /// <summary>Creates the bulb.</summary>
    /// <param name="file">A parsed, undamaged file.</param>
    /// <param name="id">The bulb id.</param>
    /// <param name="origin">Bundled or My Bulbs.</param>
    /// <param name="contentKey">The cache key of its art.</param>
    /// <param name="knownFacts">Facts per entry index already known (from the index), or null.</param>
    /// <param name="cache">The decoded-frame cache.</param>
    /// <exception cref="ArgumentException"><paramref name="file"/> is damaged.</exception>
    public AddOnBulb(
        BulFile file, string id, BulbOrigin origin, string contentKey, IReadOnlyDictionary<int, AnimationFacts>? knownFacts, DecodedArtCache cache)
    {
        if (file.IsDamaged)
        {
            throw new ArgumentException($"The bulb file is damaged: {file.Problems[0]}", nameof(file));
        }

        this.file = file;
        this.cache = cache;
        Id = id;
        Origin = origin;
        ContentKey = contentKey;
        Categories = [.. file.Categories.Where(c => c is not ("_0" or "_1"))];
        if (knownFacts is not null)
        {
            foreach ((int index, AnimationFacts value) in knownFacts)
            {
                if (index is >= 0 and < BulFile.EntryCount)
                {
                    facts[index] = value;
                }
            }
        }

        hasDamagedArt = new Lazy<bool>(() => ReferencedEntries(file).Any(e => Facts(e).IsDamaged));
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public int LegacyId => file.LegacyId;

    /// <inheritdoc />
    public BulbOrigin Origin { get; }

    /// <inheritdoc />
    public string ContentKey { get; }

    /// <inheritdoc />
    public string Name => file.Name;

    /// <inheritdoc />
    public string Description => HasDamagedArt ? DamagedDescription : file.Description;

    /// <inheritdoc />
    public string Author => file.Author;

    /// <inheritdoc />
    public string Copyright => file.Copyright;

    /// <inheritdoc />
    public IReadOnlyList<string> Categories { get; }

    /// <inheritdoc />
    public string? FilePath => file.FilePath;

    /// <inheritdoc />
    public bool IsLocked => file.Locked;

    /// <inheritdoc />
    public bool IsEditable => Origin == BulbOrigin.UserAddOn && !file.Locked;

    /// <inheritdoc />
    public bool HasDamagedArt => hasDamagedArt.Value;

    /// <inheritdoc />
    public int HorizontalSpacing => 0;

    /// <inheritdoc />
    public int VerticalSpacing => 0;

    /// <inheritdoc />
    public RectI PreviewWindow => GetPreviewWindow(file, Facts(file.PreviewEntry));

    /// <summary>The entries every drawable slot uses: preview, 4 corners, the flavors of the 4 sides.</summary>
    /// <param name="file">The file.</param>
    /// <returns>Entry indices, possibly repeated or out of range.</returns>
    public static IEnumerable<int> ReferencedEntries(BulFile file)
    {
        yield return file.PreviewEntry;
        foreach (int corner in file.CornerEntries)
        {
            yield return corner;
        }

        foreach (Side side in CellSlots.Sides)
        {
            for (int flavor = 0; flavor < file.GetFlavorCount(side); flavor++)
            {
                yield return file.SideEntries[(int)side][flavor];
            }
        }
    }

    /// <summary>
    /// The 32 x 32 list-preview window: <c>{x, y, min(x + 32, W), min(y + 32, H)}</c> of frame 0,
    /// kept inside the picture; the whole WARNING picture for damaged art.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="preview">Facts of the preview entry.</param>
    /// <returns>The window in preview-cell coordinates.</returns>
    private static RectI GetPreviewWindow(BulFile file, AnimationFacts preview)
    {
        if (preview.IsDamaged)
        {
            return new RectI(0, 0, preview.Width, preview.Height);
        }

        int x = Math.Clamp(file.PreviewX, 0, preview.Width);
        int y = Math.Clamp(file.PreviewY, 0, preview.Height);
        return new RectI(x, y, Math.Min(x + 32, preview.Width), Math.Min(y + 32, preview.Height));
    }

    /// <inheritdoc />
    public int GetFlavorCount(Side side) => file.GetFlavorCount(side);

    /// <inheritdoc />
    public int GetPhaseCount(Side side)
    {
        int phases = 1;
        for (int flavor = 0; flavor < file.GetFlavorCount(side); flavor++)
        {
            phases = Math.Max(phases, Facts(file.SideEntries[(int)side][flavor]).FrameCount);
        }

        return phases;
    }

    /// <inheritdoc />
    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor) => Facts(file.GetEntryIndex(slot, flavor)).ToInfo();

    /// <inheritdoc />
    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => Facts(file.GetEntryIndex(slot, flavor)).Size;

    /// <inheritdoc />
    public BulbCell GetCell(CellSlot slot, int flavor, int phase)
    {
        int entry = file.GetEntryIndex(slot, flavor);
        if (Facts(entry).IsDamaged)
        {
            return new BulbCell(HeritageArt.Warning, null, true);
        }

        Rgba32Image[] frames = cache.GetOrAdd(CacheKey(entry), () => Decode(entry));
        return frames.Length == 0
            ? new BulbCell(HeritageArt.Warning, null, true)
            : new BulbCell(frames[((phase % frames.Length) + frames.Length) % frames.Length], null, false);
    }

    /// <summary>The facts of one entry (decoding it once when they are not known yet).</summary>
    /// <param name="entry">An entry index as stored in the slot table.</param>
    /// <returns>The facts; out-of-range and empty entries are damaged.</returns>
    private AnimationFacts Facts(int entry)
    {
        if (entry is < 0 or >= BulFile.EntryCount)
        {
            return AnimationFacts.Damaged;
        }

        lock (gate)
        {
            if (facts[entry] is { } known)
            {
                return known;
            }
        }

        AnimationFacts computed = AnimationFacts.Analyze(file.Entries[entry].Gif.Span, keepFrames: true, out Rgba32Image[]? frames);
        if (frames is not null)
        {
            cache.Add(CacheKey(entry), frames);
        }

        lock (gate)
        {
            return facts[entry] ??= computed;
        }
    }

    private Rgba32Image[] Decode(int entry)
    {
        AnimationFacts.Analyze(file.Entries[entry].Gif.Span, keepFrames: true, out Rgba32Image[]? frames);
        return frames ?? [];
    }

    private string CacheKey(int entry) => $"{ContentKey}#{entry}";
}
