namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// An editable bulb (the Bulb Editing model): text fields, categories, the slot table
/// (preview, 4 corners, 4 sides x 8 flavors) and the GIFs it references, de-duplicated by CRC. Owner: bulb-factory.
/// </summary>
/// <remarks>
/// <para>Mutable; used on the UI thread by one editor. <see cref="BulFileWriter"/> turns it into a 5.4-compatible file.</para>
/// <para>Every slot refers to a <see cref="BulbGif"/>; identical GIFs are stored once. A side may have gaps while it is
/// edited (5.4's "Remove Flavor" empties one flavor slot); the writer packs each side's flavors to the front, as 5.4's Save
/// did. The GIF table (<see cref="Gifs"/>, the entry order of the written file) lists every GIF in use in the order it
/// was first used; a GIF that is no longer used leaves it.</para>
/// </remarks>
public sealed class BulbDocument
{
    /// <summary>The most different GIFs a bulb file can hold.</summary>
    public const int MaxAnimations = 37;

    /// <summary>Most flavors per side.</summary>
    public const int MaxFlavors = 8;

    /// <summary>Longest text field (79 characters + NUL in the file).</summary>
    public const int MaxTextLength = BulText.MaxFieldLength;

    /// <summary>Size of the list-preview window (5.4 Bulb List picture).</summary>
    private const int PreviewWindowSize = 32;

    private readonly List<BulbGif> gifs = [];
    private readonly BulbGif?[] corners = new BulbGif?[4];
    private readonly BulbGif?[][] sides = [new BulbGif?[MaxFlavors], new BulbGif?[MaxFlavors], new BulbGif?[MaxFlavors], new BulbGif?[MaxFlavors]];
    private BulbGif? preview;

    /// <summary>Creates an empty document (no animations yet).</summary>
    public BulbDocument()
    {
    }

    /// <summary>"Bulb Name" (required, at most 79 characters).</summary>
    public string Name { get; set; } = "";

    /// <summary>"Description".</summary>
    public string Description { get; set; } = "";

    /// <summary>"Author's Name and E-Mail Address" (two lines joined with "\r\n").</summary>
    public string Author { get; set; } = "";

    /// <summary>"Copyright Message" (two lines joined with "\r\n").</summary>
    public string Copyright { get; set; } = "";

    /// <summary>Checked categories.</summary>
    public IList<string> Categories { get; } = new List<string>();

    /// <summary>Left of the 32 x 32 list-preview window (header <c>previewX</c>).</summary>
    public int PreviewX { get; set; }

    /// <summary>Top of the list-preview window (header <c>previewY</c>).</summary>
    public int PreviewY { get; set; }

    /// <summary>Number of different GIFs referenced (at most <see cref="MaxAnimations"/>).</summary>
    public int AnimationCount => gifs.Count;

    /// <summary>The GIFs in use, in the order they were first used (the entry order of the written file).</summary>
    public IReadOnlyList<BulbGif> Gifs => gifs;

    /// <summary>
    /// The 32 x 32 list-preview window inside frame 0 of the preview animation (smaller when the picture is):
    /// <see cref="PreviewX"/> and <see cref="PreviewY"/> kept inside the range the 5.4 editor allows
    /// (<c>0..W - min(W, 32)</c>, <c>0..H - min(H, 32)</c>).
    /// </summary>
    public RectI PreviewWindow
    {
        get
        {
            SizeI size = preview?.Size ?? default;
            int width = Math.Min(Math.Max(size.Width, 0), PreviewWindowSize);
            int height = Math.Min(Math.Max(size.Height, 0), PreviewWindowSize);
            int x = Math.Clamp(PreviewX, 0, Math.Max(size.Width - width, 0));
            int y = Math.Clamp(PreviewY, 0, Math.Max(size.Height - height, 0));
            return new RectI(x, y, x + width, y + height);
        }
    }

    /// <summary>True when every slot 5.4 requires has an animation: the preview, the 4 corners and flavor 1 of each side.</summary>
    public bool IsComplete => !MissingSlots.Any();

    /// <summary>The required slots without an animation (a side counts when it has no flavor at all).</summary>
    public IEnumerable<CellSlot> MissingSlots
    {
        get
        {
            foreach (Side side in CellSlots.Sides)
            {
                if (GetFlavorCount(side) == 0)
                {
                    yield return side.ToSlot();
                }
            }

            foreach (Corner corner in CellSlots.Corners)
            {
                if (corners[(int)corner] is null)
                {
                    yield return corner.ToSlot();
                }
            }

            if (preview is null)
            {
                yield return CellSlot.Preview;
            }
        }
    }

    /// <summary>Loads an editable copy of a file (only files with <c>locked</c> = 0 are edited).</summary>
    /// <remarks>
    /// The slots take what 5.4 draws: flavors after a side's first unused flavor are ignored, and a slot whose entry is
    /// empty or out of range stays empty. Equal GIFs stored in several entries become one. The internal categories
    /// <c>_0</c> and <c>_1</c> are dropped.
    /// </remarks>
    /// <param name="file">A parsed, undamaged file.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> is damaged.</exception>
    public static BulbDocument FromFile(BulFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.IsDamaged)
        {
            throw new ArgumentException($"The bulb file is damaged: {file.Problems[0]}", nameof(file));
        }

        var document = new BulbDocument
        {
            Name = file.Name,
            Description = file.Description,
            Author = file.Author,
            Copyright = file.Copyright,
            PreviewX = file.PreviewX,
            PreviewY = file.PreviewY,
        };
        foreach (string category in file.Categories.Where(c => c is not ("_0" or "_1")))
        {
            document.Categories.Add(category);
        }

        Dictionary<int, BulbGif> byEntry = LoadReferencedEntries(file);
        BulbGif? Entry(int index) => byEntry.GetValueOrDefault(index);
        document.preview = Entry(file.PreviewEntry);
        for (int c = 0; c < document.corners.Length; c++)
        {
            document.corners[c] = Entry(file.CornerEntries[c]);
        }

        foreach (Side side in CellSlots.Sides)
        {
            for (int flavor = 0; flavor < file.GetFlavorCount(side); flavor++)
            {
                document.sides[(int)side][flavor] = Entry(file.SideEntries[(int)side][flavor]);
            }
        }

        document.gifs.AddRange(byEntry.OrderBy(e => e.Key).Select(e => e.Value).Distinct());
        return document;
    }

    /// <summary>Returns the GIF bytes of a slot and flavor, or null when the flavor is empty.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Flavor 0-7 for sides; 0 for corners and the preview.</param>
    /// <returns>The GIF bytes, or null.</returns>
    public ReadOnlyMemory<byte>? GetAnimation(CellSlot slot, int flavor) => GetGif(slot, flavor)?.Bytes;

    /// <summary>Returns the GIF of a slot and flavor, or null when the flavor is empty.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Flavor 0-7 for sides; ignored for corners and the preview.</param>
    /// <returns>The GIF, or null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A side flavor outside 0-7.</exception>
    public BulbGif? GetGif(CellSlot slot, int flavor) => slot switch
    {
        CellSlot.Preview => preview,
        _ when slot.IsCorner() => corners[(int)slot.ToCorner()],
        _ => sides[(int)slot.ToSide()][CheckFlavor(flavor, 0)],
    };

    /// <summary>"Change...": uses a GIF for a slot and flavor (identical GIFs are stored once, by CRC).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Flavor 0-7 for sides; 0 otherwise.</param>
    /// <param name="gif">The GIF bytes.</param>
    /// <returns>False when the bulb already holds 37 different GIFs and this one is new (which cannot happen, see <see cref="SetAnimation(CellSlot, int, BulbGif)"/>).</returns>
    /// <exception cref="InvalidDataException">The faithful decoder cannot use the GIF ("Cannot Import GIF File").</exception>
    public bool SetAnimation(CellSlot slot, int flavor, ReadOnlyMemory<byte> gif) => SetAnimation(slot, flavor, BulbGif.FromBytes(gif.Span));

    /// <summary>"Change...": uses a checked GIF for a slot and flavor (identical GIFs are stored once).</summary>
    /// <remarks>A new preview animation centres the list-preview window (5.4).</remarks>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Flavor 0-7 for sides; ignored otherwise.</param>
    /// <param name="gif">The GIF.</param>
    /// <returns>
    /// Always true. A bulb has 37 slots (preview, 4 corners, 4 sides x 8 flavors) and a replaced GIF leaves the table at
    /// once, so a document never uses more than the <see cref="MaxAnimations"/> entries a file holds. (5.4 kept a replaced
    /// GIF until it saved and refused a new one in a bulb with 37 different animations.)
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">A side flavor outside 0-7.</exception>
    public bool SetAnimation(CellSlot slot, int flavor, BulbGif gif)
    {
        ArgumentNullException.ThrowIfNull(gif);
        BulbGif shared = gifs.FirstOrDefault(g => g.Equals(gif)) ?? gif;
        Assign(slot, flavor, shared);
        if (!gifs.Contains(shared))
        {
            gifs.Add(shared);
        }

        RemoveUnusedGifs();
        if (slot == CellSlot.Preview)
        {
            CenterPreviewWindow();
        }

        return true;
    }

    /// <summary>"Remove Flavor" (flavors 2-8 of a side; flavor 1 cannot be removed).</summary>
    /// <param name="side">The side.</param>
    /// <param name="flavor">Flavor 1-7 (0-based).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="flavor"/> is not 1-7.</exception>
    public void RemoveFlavor(Side side, int flavor)
    {
        sides[(int)side][CheckFlavor(flavor, 1)] = null;
        RemoveUnusedGifs();
    }

    /// <summary>"Copy to All Sides": the other three sides get this side's flavors.</summary>
    /// <param name="side">The source side.</param>
    public void CopyToAllSides(Side side)
    {
        BulbGif?[] source = sides[(int)side];
        foreach (Side other in CellSlots.Sides.Where(s => s != side))
        {
            source.CopyTo(sides[(int)other], 0);
        }

        RemoveUnusedGifs();
    }

    /// <summary>Number of flavors of a side that have an animation (what the side has once its flavors are packed).</summary>
    /// <param name="side">The side.</param>
    /// <returns>0-8.</returns>
    public int GetFlavorCount(Side side) => sides[(int)side].Count(g => g is not null);

    /// <summary>The flavors of a side packed to the front (gaps removed, order kept), as the writer stores them.</summary>
    /// <param name="side">The side.</param>
    /// <returns>The GIFs of flavors 1, 2, ... .</returns>
    public IReadOnlyList<BulbGif> GetFlavors(Side side) => [.. sides[(int)side].OfType<BulbGif>()];

    /// <summary>Centres the list-preview window on frame 0 of the preview animation (5.4: <c>W &gt;= 32 ? W/2 - 16 : 0</c>, same for Y).</summary>
    public void CenterPreviewWindow()
    {
        SizeI size = preview?.Size ?? default;
        PreviewX = size.Width >= PreviewWindowSize ? size.Width / 2 - PreviewWindowSize / 2 : 0;
        PreviewY = size.Height >= PreviewWindowSize ? size.Height / 2 - PreviewWindowSize / 2 : 0;
    }

    /// <summary>Moves the list-preview window (the white square), kept inside the picture like the 5.4 editor's drag handler.</summary>
    /// <param name="x">The new left.</param>
    /// <param name="y">The new top.</param>
    public void MovePreviewWindow(int x, int y)
    {
        PreviewX = x;
        PreviewY = y;
        RectI window = PreviewWindow;
        PreviewX = window.Left;
        PreviewY = window.Top;
    }

    /// <summary>Returns an independent copy (the GIFs are immutable and shared).</summary>
    /// <returns>The copy.</returns>
    public BulbDocument Clone()
    {
        var copy = new BulbDocument
        {
            Name = Name,
            Description = Description,
            Author = Author,
            Copyright = Copyright,
            PreviewX = PreviewX,
            PreviewY = PreviewY,
            preview = preview,
        };
        foreach (string category in Categories)
        {
            copy.Categories.Add(category);
        }

        corners.CopyTo(copy.corners, 0);
        for (int s = 0; s < sides.Length; s++)
        {
            sides[s].CopyTo(copy.sides[s], 0);
        }

        copy.gifs.AddRange(gifs);
        return copy;
    }

    /// <summary>
    /// True when saving both documents gives the same bulb: equal texts and categories (ordinal, in order), the same
    /// list-preview window and the same GIF in every slot once the sides are packed.
    /// </summary>
    /// <param name="other">The other document.</param>
    /// <returns>True when nothing differs.</returns>
    public bool HasSameContent(BulbDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Name == other.Name
            && Description == other.Description
            && Author == other.Author
            && Copyright == other.Copyright
            && Categories.SequenceEqual(other.Categories, StringComparer.Ordinal)
            && PreviewWindow == other.PreviewWindow
            && Equals(preview, other.preview)
            && Enumerable.SequenceEqual(corners, other.corners)
            && CellSlots.Sides.All(side => GetFlavors(side).SequenceEqual(other.GetFlavors(side)));
    }

    private static int CheckFlavor(int flavor, int lowest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(flavor, lowest);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(flavor, MaxFlavors);
        return flavor;
    }

    /// <summary>The GIFs of the entries the drawable slots refer to, by entry index (equal content shares one instance).</summary>
    private static Dictionary<int, BulbGif> LoadReferencedEntries(BulFile file)
    {
        var referenced = new SortedSet<int> { file.PreviewEntry };
        referenced.UnionWith(file.CornerEntries);
        foreach (Side side in CellSlots.Sides)
        {
            referenced.UnionWith(file.SideEntries[(int)side].Take(file.GetFlavorCount(side)));
        }

        var byEntry = new Dictionary<int, BulbGif>();
        var distinct = new List<BulbGif>();
        foreach (int index in referenced)
        {
            if (index is < 0 or >= BulFile.EntryCount || file.Entries[index].Gif.IsEmpty)
            {
                continue;
            }

            BulbGif gif = BulbGif.FromEntry(file.Entries[index]);
            BulbGif? same = distinct.FirstOrDefault(g => g.Equals(gif));
            if (same is null)
            {
                distinct.Add(gif);
            }

            byEntry[index] = same ?? gif;
        }

        return byEntry;
    }

    private void Assign(CellSlot slot, int flavor, BulbGif gif)
    {
        if (slot == CellSlot.Preview)
        {
            preview = gif;
        }
        else if (slot.IsCorner())
        {
            corners[(int)slot.ToCorner()] = gif;
        }
        else
        {
            sides[(int)slot.ToSide()][CheckFlavor(flavor, 0)] = gif;
        }
    }

    private IEnumerable<BulbGif?> AllSlots() => [preview, .. corners, .. sides.SelectMany(s => s)];

    private void RemoveUnusedGifs()
    {
        var used = new HashSet<BulbGif>(AllSlots().OfType<BulbGif>(), ReferenceEqualityComparer.Instance);
        gifs.RemoveAll(g => !used.Contains(g));
    }
}
