using System.Collections.Concurrent;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Catalog;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

/// <summary>
/// A catalog over a private data root: a chosen set of bundled bulbs (or all 1,501), in-memory settings, a test holding
/// folder, a fixed clock, a fake GIF writer and a record of every change event.
/// </summary>
internal sealed class CatalogHarness : IDisposable
{
    /// <summary>A few bundled bulbs with useful names, categories and art.</summary>
    public static readonly string[] SmallBundledSet =
    [
        "Arrow.bul", "10thBirthday.bul", "Snow.bul", "Snow2.bul", "Snowman.bul", "Snowman3.bul", "HalloweenBulbs.bul",
        "MulticolorBubbleLights.bul", "Gemstones.bul", "BunnyWithFlower.bul",
    ];

    private readonly TempDataRoot root = new();
    private readonly BlockingCollection<BulbCatalogChangedEventArgs> events = [];

    /// <summary>Creates the harness.</summary>
    /// <param name="bundled">Bundled file names to install, or null for every bundled bulb.</param>
    /// <param name="settings">Initial settings.</param>
    public CatalogHarness(IEnumerable<string>? bundled = null, AppSettings? settings = null)
    {
        string install = bundled is null ? TestPaths.OutputFolder : Path.Combine(root.Root, "Install");
        if (bundled is not null)
        {
            string folder = Directory.CreateDirectory(Path.Combine(install, "Content", "Bulbs")).FullName;
            foreach (string name in bundled)
            {
                File.Copy(Path.Combine(BulbGoldens.BundledBulbsFolder, name), Path.Combine(folder, name));
            }
        }

        Paths = DataPaths.ForDataRoot(root.Root, install);
        Settings = new InMemorySettingsStore(settings);
        Holding = new TestHoldingFolder(Paths);
        Catalog = Create();
    }

    /// <summary>The data paths.</summary>
    public DataPaths Paths { get; }

    /// <summary>The settings.</summary>
    public InMemorySettingsStore Settings { get; }

    /// <summary>The log.</summary>
    public RecordingLog Log { get; } = new();

    /// <summary>The holding folder.</summary>
    public TestHoldingFolder Holding { get; }

    /// <summary>The fake GIF writer.</summary>
    public FakeGifWriter GifWriter { get; } = new();

    /// <summary>The clock (2026-10-08 12:00 UTC).</summary>
    public static DateTimeOffset Now { get; } = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The catalog.</summary>
    public BulbCatalog Catalog { get; private set; }

    /// <summary>Creates a catalog over the same folders (a second start).</summary>
    /// <returns>The new catalog; the harness disposes it.</returns>
    public BulbCatalog Restart()
    {
        Catalog.Dispose();
        Catalog = Create();
        return Catalog;
    }

    /// <summary>Starts the catalog and waits for indexing.</summary>
    /// <returns>The catalog.</returns>
    public BulbCatalog Start()
    {
        Assert.True(Catalog.StartAsync().Wait(TimeSpan.FromSeconds(30)), "Indexing did not finish.");
        return Catalog;
    }

    /// <summary>Waits for a change event (events are delivered asynchronously).</summary>
    /// <param name="change">The kind.</param>
    /// <param name="id">An id the event must name, or null.</param>
    /// <returns>The event.</returns>
    public BulbCatalogChangedEventArgs WaitFor(BulbCatalogChange change, string? id = null)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (events.TryTake(out BulbCatalogChangedEventArgs? e, TimeSpan.FromMilliseconds(100))
                && e.Change == change && (id is null || e.BulbIds.Contains(id, BulbIds.Comparer)))
            {
                return e;
            }
        }

        throw new TimeoutException($"No {change} event{(id is null ? "" : " for " + id)}.");
    }

    /// <summary>Forgets the events received so far.</summary>
    public void ClearEvents()
    {
        while (events.TryTake(out _))
        {
        }
    }

    /// <summary>Writes a <c>.bul</c> into My Bulbs.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="bul">The file.</param>
    /// <returns>The path.</returns>
    public string WriteMyBulb(string fileName, TestBul bul) => bul.WriteTo(Path.Combine(Paths.MyBulbsFolder, fileName));

    /// <summary>A path in a scratch folder of the data root (outside My Bulbs).</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The path; the folder exists.</returns>
    public string Scratch(string fileName) => Path.Combine(Directory.CreateDirectory(Path.Combine(root.Root, "Scratch")).FullName, fileName);

    /// <inheritdoc />
    public void Dispose()
    {
        Catalog.Dispose();
        events.Dispose();
        root.Dispose();
    }

    private BulbCatalog Create()
    {
        var catalog = new BulbCatalog(
            Paths, Settings, Holding, Log, () => "Pat Smith", new FixedTime(Now), GifWriter, new DecodedArtCache(16L * 1024 * 1024));
        catalog.Changed += (_, e) => events.Add(e);
        return catalog;
    }

    /// <summary>A clock that always says <see cref="Now"/> (in UTC).</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

/// <summary>Turns a GIF into a one-entry, editable bulb file like the Bulb Factory writer would.</summary>
internal sealed class FakeGifWriter : IGifBulbWriter
{
    /// <summary>The calls (author, year, bulb id).</summary>
    public List<(string Author, int Year, int BulbId)> Calls { get; } = [];

    /// <inheritdoc />
    public string Write(string gifPath, string folder, string authorName, int year, int bulbId)
    {
        byte[] gif = File.ReadAllBytes(gifPath);
        GifDecoder.DecodeClassic(gif);
        Calls.Add((authorName, year, bulbId));
        string name = Path.GetFileNameWithoutExtension(gifPath);
        string path = Path.Combine(folder, name + ".bul");
        for (int n = 1; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{name} {n}.bul");
        }

        return new TestBul
        {
            Gifs = [gif],
            Name = name,
            Locked = false,
            BulbId = (uint)bulbId,
            Author = authorName,
            Copyright = $"Copyright {year} {authorName}",
            Description = "No description is available for this bulb.",
            Categories = null,
        }.WriteTo(path);
    }
}
