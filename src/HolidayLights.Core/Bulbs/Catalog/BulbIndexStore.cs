using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// The add-on index cache (<c>%LOCALAPPDATA%\Holiday Lights\Cache\index.json</c>, PRODUCT-SPEC 3.2.5 and 6.3): what was
/// learned from each file (metadata, content identity, the facts of its animations, or why it is damaged), valid while
/// the file keeps its size and last write time.
/// </summary>
/// <remarks>A missing, unreadable or outdated cache is simply rebuilt; it holds nothing that cannot be recomputed.</remarks>
internal sealed class BulbIndexStore(string path, IAppLog log)
{
    /// <summary>The cache format; bump it whenever what is cached or how it is computed changes.</summary>
    public const string Schema = "holidaylights.bulb-index/1";

    private const string LogSource = "Bulbs.Index";

    /// <summary>Reads the cache.</summary>
    /// <returns>Records by <see cref="BulbIndexRecord.Key"/>; empty when there is no usable cache.</returns>
    public Dictionary<string, BulbIndexRecord> Load()
    {
        var records = new Dictionary<string, BulbIndexRecord>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path))
            {
                return records;
            }

            using FileStream stream = File.OpenRead(path);
            BulbIndexDocument? document = JsonSerializer.Deserialize(stream, BulbIndexJsonContext.Default.BulbIndexDocument);
            if (document?.Schema != Schema)
            {
                return records;
            }

            foreach (BulbIndexRecord record in document.Files)
            {
                records[record.Key] = record;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            log.Warn(LogSource, "The bulb index cache could not be read; it will be rebuilt.", e);
            records.Clear();
        }

        return records;
    }

    /// <summary>Writes the cache atomically (temporary file, then replace).</summary>
    /// <param name="records">Every indexed file.</param>
    public void Save(IEnumerable<BulbIndexRecord> records)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var document = new BulbIndexDocument { Schema = Schema, Files = [.. records.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)] };
            using (FileStream stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, document, BulbIndexJsonContext.Default.BulbIndexDocument);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "The bulb index cache could not be written.", e);
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left behind; the next save overwrites it.
        }
    }
}

/// <summary>The cache file.</summary>
internal sealed class BulbIndexDocument
{
    /// <summary>The format (<see cref="BulbIndexStore.Schema"/>).</summary>
    public string Schema { get; init; } = "";

    /// <summary>One record per indexed file.</summary>
    public IReadOnlyList<BulbIndexRecord> Files { get; init; } = [];
}

/// <summary>What was learned from one <c>.bul</c> file.</summary>
internal sealed class BulbIndexRecord
{
    /// <summary>Bundled or My Bulbs.</summary>
    public BulbOrigin Origin { get; init; }

    /// <summary>The file name (the folder follows from the origin).</summary>
    public string File { get; init; } = "";

    /// <summary>File size when indexed.</summary>
    public long Size { get; init; }

    /// <summary>Last write time (UTC ticks) when indexed.</summary>
    public long LastWrite { get; init; }

    /// <summary>Why the file cannot be loaded, or null for a good file.</summary>
    public string? Damaged { get; init; }

    /// <summary>Header bulb id.</summary>
    public int LegacyId { get; init; }

    /// <summary>Header <c>locked</c>.</summary>
    public bool Locked { get; init; }

    /// <summary>Name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Description as stored in the file.</summary>
    public string Description { get; init; } = "";

    /// <summary>Author.</summary>
    public string Author { get; init; } = "";

    /// <summary>Copyright.</summary>
    public string Copyright { get; init; } = "";

    /// <summary>Categories of the <c>categ:</c> record.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Content identity.</summary>
    public string Identity { get; init; } = "";

    /// <summary>Flavors on the top edge.</summary>
    public int TopFlavorCount { get; init; }

    /// <summary>Entry index of each top-edge flavor.</summary>
    public IReadOnlyList<int> TopEntries { get; init; } = [];

    /// <summary>Every entry index the slot table references (with repeats and out-of-range values as stored).</summary>
    public IReadOnlyList<int> ReferencedEntries { get; init; } = [];

    /// <summary>Facts of the referenced in-range entries.</summary>
    public IReadOnlyList<BulbIndexFacts> Entries { get; init; } = [];

    /// <summary>The cache key: origin and file name.</summary>
    [JsonIgnore]
    public string Key => KeyFor(Origin, File);

    /// <summary>Builds a cache key.</summary>
    /// <param name="origin">Bundled or My Bulbs.</param>
    /// <param name="fileName">The file name.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(BulbOrigin origin, string fileName) => $"{origin}/{fileName}";

    /// <summary>Records a good file.</summary>
    /// <param name="entry">Its catalog entry (an add-on with a header).</param>
    /// <returns>The record.</returns>
    public static BulbIndexRecord FromEntry(CatalogEntry entry)
    {
        AddOnHeader header = entry.Header ?? throw new ArgumentException("Only add-on entries are cached.", nameof(entry));
        return new BulbIndexRecord
        {
            Origin = entry.Origin,
            File = Path.GetFileName(entry.FilePath!),
            Size = entry.FileSize,
            LastWrite = entry.LastWriteUtc!.Value.Ticks,
            LegacyId = header.LegacyId,
            Locked = header.Locked,
            Name = header.Name,
            Description = header.Description,
            Author = header.Author,
            Copyright = header.Copyright,
            Categories = header.Categories,
            Identity = header.Identity,
            TopEntries = header.TopEntries,
            ReferencedEntries = header.ReferencedEntries,
            Entries = [.. entry.Facts.OrderBy(f => f.Key).Select(f => new BulbIndexFacts
            {
                Index = f.Key,
                Width = f.Value.Width,
                Height = f.Value.Height,
                Frames = f.Value.FrameCount,
                Kind = f.Value.Kind,
                LitFrame = f.Value.LitFrame,
                Damaged = f.Value.IsDamaged,
            })],
        };
    }

    /// <summary>Records a damaged file.</summary>
    /// <param name="origin">Bundled or My Bulbs.</param>
    /// <param name="stamp">The file.</param>
    /// <param name="reason">Why it cannot be loaded.</param>
    /// <returns>The record.</returns>
    public static BulbIndexRecord FromDamaged(BulbOrigin origin, FileStamp stamp, string reason) => new()
    {
        Origin = origin,
        File = Path.GetFileName(stamp.Path),
        Size = stamp.Size,
        LastWrite = stamp.LastWriteUtc.Ticks,
        Damaged = reason,
    };

    /// <summary>True when the record describes the file as it is now (same size and last write time).</summary>
    /// <param name="stamp">The file now.</param>
    /// <returns>True when the record can be used.</returns>
    public bool Describes(FileStamp stamp) => Size == stamp.Size && LastWrite == stamp.LastWriteUtc.Ticks;

    /// <summary>Rebuilds the catalog entry of a good file.</summary>
    /// <param name="id">The bulb id.</param>
    /// <param name="stamp">The file now.</param>
    /// <returns>The entry.</returns>
    public CatalogEntry ToEntry(string id, FileStamp stamp)
    {
        var header = new AddOnHeader(LegacyId, Locked, Name, Description, Author, Copyright, Categories, Identity, TopEntries, ReferencedEntries);
        var facts = Entries.ToDictionary(
            f => f.Index,
            f => f.Damaged ? AnimationFacts.Damaged : new AnimationFacts(f.Width, f.Height, f.Frames, f.Kind, f.LitFrame, false));
        return CatalogEntry.FromAddOn(header, id, Origin, stamp, facts);
    }
}

/// <summary>Facts of one GIF entry.</summary>
internal sealed class BulbIndexFacts
{
    /// <summary>Entry index.</summary>
    public int Index { get; init; }

    /// <summary>Frame width.</summary>
    public int Width { get; init; }

    /// <summary>Frame height.</summary>
    public int Height { get; init; }

    /// <summary>Frame count.</summary>
    public int Frames { get; init; }

    /// <summary>Kind.</summary>
    public BulbAnimationKind Kind { get; init; }

    /// <summary>Lit frame of a light bulb.</summary>
    public int LitFrame { get; init; }

    /// <summary>True when the faithful decoder rejects the GIF.</summary>
    public bool Damaged { get; init; }
}

/// <summary>Source-generated metadata for the index cache.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = false)]
[JsonSerializable(typeof(BulbIndexDocument))]
internal sealed partial class BulbIndexJsonContext : JsonSerializerContext;
