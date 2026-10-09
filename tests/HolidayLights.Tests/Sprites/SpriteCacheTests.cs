using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>The two halves of the sprite cache: the LRU memory cache and the disk store.</summary>
public sealed class SpriteCacheTests : IDisposable
{
    private readonly TempDataRoot dataRoot = new();

    public void Dispose() => dataRoot.Dispose();

    [Fact]
    public void MemoryCache_EvictsTheLeastRecentlyUsedEntryOverBudget()
    {
        SpriteCacheEntry entry = Frame(8);
        var cache = new SpriteMemoryCache(3 * entry.SizeInBytes);
        cache.Add(Key("a"), Frame(8));
        cache.Add(Key("b"), Frame(8));
        cache.Add(Key("c"), Frame(8));

        Assert.True(cache.TryGet(Key("a"), out _));
        cache.Add(Key("d"), Frame(8));

        Assert.False(cache.TryGet(Key("b"), out _));
        Assert.True(cache.TryGet(Key("a"), out _));
        Assert.True(cache.TryGet(Key("c"), out _));
        Assert.True(cache.TryGet(Key("d"), out _));
        Assert.Equal(3 * entry.SizeInBytes, cache.Bytes);
    }

    [Fact]
    public void MemoryCache_ReplacingAnEntryKeepsTheAccountingExact()
    {
        var cache = new SpriteMemoryCache(1 << 20);
        cache.Add(Key("a"), Frame(8));
        SpriteCacheEntry replacement = Frame(16);

        cache.Add(Key("a"), replacement);

        Assert.Equal(1, cache.Count);
        Assert.Equal(replacement.SizeInBytes, cache.Bytes);
        Assert.True(cache.TryGet(Key("a"), out SpriteCacheEntry? found));
        Assert.Same(replacement, found);
    }

    [Fact]
    public void MemoryCache_KeepsTheNewestEntryEvenWhenItAloneExceedsTheBudget()
    {
        var cache = new SpriteMemoryCache(100);

        cache.Add(Key("a"), Frame(64));

        Assert.True(cache.TryGet(Key("a"), out _));
    }

    [Fact]
    public void MemoryCache_RemovesEveryEntryOfAContentKey()
    {
        var cache = new SpriteMemoryCache(1 << 20);
        cache.Add(Key("a", frame: 0), Frame(4));
        cache.Add(Key("a", frame: 1), Frame(4));
        cache.Add(SpriteCacheKey.ForGlow("a", CellSlot.Top, 0, 1.5), SpriteCacheEntry.ForGlow(null));
        cache.Add(Key("b"), Frame(4));

        Assert.Equal(3, cache.RemoveContent("a"));
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(Key("b"), out _));
    }

    [Fact]
    public void DiskStore_RoundTripsFramesHalosAndMissingHalos()
    {
        SpriteDiskStore store = NewStore();
        var frame = new PremultipliedImage(3, 2, [1, 2, 3, 0xFF000004, 5, 0x80402010]);
        var glow = new GlowSprite(new PremultipliedImage(2, 2, [0x00102030, 0, 7, 0x00FFFFFF]), -5, -6);

        Assert.True(store.TryWrite(Key("a"), SpriteCacheEntry.ForFrame(frame)));
        Assert.True(store.TryWrite(SpriteCacheKey.ForGlow("a", CellSlot.Top, 0, 1.5), SpriteCacheEntry.ForGlow(glow)));
        Assert.True(store.TryWrite(SpriteCacheKey.ForGlow("a", CellSlot.Left, 1, 1.5), SpriteCacheEntry.ForGlow(null)));

        Assert.True(store.TryRead(Key("a"), out SpriteCacheEntry? readFrame));
        Assert.True(readFrame.IsPersisted);
        Assert.Equal(frame.Pixels, readFrame.Frame!.Pixels);
        Assert.Equal(frame.Size, readFrame.Frame.Size);
        Assert.True(store.TryRead(SpriteCacheKey.ForGlow("a", CellSlot.Top, 0, 1.5), out SpriteCacheEntry? readGlow));
        Assert.Equal((-5, -6), (readGlow.Glow!.OffsetX, readGlow.Glow.OffsetY));
        Assert.Equal(glow.Image.Pixels, readGlow.Glow.Image.Pixels);
        Assert.True(store.TryRead(SpriteCacheKey.ForGlow("a", CellSlot.Left, 1, 1.5), out SpriteCacheEntry? noGlow));
        Assert.Null(noGlow.Glow);
        Assert.False(store.TryRead(Key("a", frame: 1), out _));
    }

    [Fact]
    public void DiskStore_RejectsAFileWrittenForAnotherKey()
    {
        SpriteDiskStore store = NewStore();
        store.TryWrite(Key("a"), Frame(4));
        string other = store.GetPath(Key("a", frame: 1));
        File.Copy(store.GetPath(Key("a")), other);

        Assert.False(store.TryRead(Key("a", frame: 1), out _));
    }

    [Fact]
    public void DiskStore_KeysScalesByTheirExactValue()
    {
        Assert.NotEqual(Key("a", scale: 1.5).ToFileName(), Key("a", scale: 1.5000000000000002).ToFileName());
        Assert.Equal("frame-0-0-0-3ff8000000000000-smooth.sprite", Key("a", scale: 1.5).ToFileName());
    }

    [Fact]
    public async Task DiskStore_PruneDeletesTheOldestFilesDownToThreeQuartersOfTheBudget()
    {
        // Deterministic (product-owner decision 6): the files are written by a store whose budget its background trims
        // never reach, and their times are set once those trims are done; the store under test has queued nothing.
        SpriteDiskStore writer = NewStore();
        var keys = Enumerable.Range(0, 8).Select(i => Key("bulb" + i)).ToArray();
        foreach (SpriteCacheKey key in keys)
        {
            Assert.True(writer.TryWrite(key, Frame(20))); // about 1,700 bytes each
        }

        await writer.Maintenance;
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < keys.Length; i++)
        {
            File.SetLastWriteTimeUtc(writer.GetPath(keys[i]), now.AddMinutes(i - keys.Length));
        }

        SpriteDiskStore store = NewStore(budgetBytes: 10_000);
        string staleTemporary = store.GetPath(keys[0]) + ".abc.tmp";
        File.WriteAllText(staleTemporary, "x");
        File.SetLastWriteTimeUtc(staleTemporary, now.AddHours(-2));
        string oldVersion = Path.Combine(Path.GetDirectoryName(store.Root)!, "v0");
        Directory.CreateDirectory(oldVersion);
        File.WriteAllText(Path.Combine(oldVersion, "old.sprite"), "x");

        store.Prune();

        long total = keys.Where(k => File.Exists(store.GetPath(k))).Sum(k => new FileInfo(store.GetPath(k)).Length);
        Assert.True(total <= 7_500, $"{total} bytes left");
        Assert.False(File.Exists(store.GetPath(keys[0])));
        Assert.True(File.Exists(store.GetPath(keys[^1])));
        Assert.False(File.Exists(staleTemporary));
        Assert.False(Directory.Exists(oldVersion));
    }

    [Fact]
    public async Task DiskStore_KeepsTrimmingToTheBudgetDuringALongSession()
    {
        // Regression: only the first write of a process used to queue a trim, so the cache grew without limit until the
        // next start. Every write here comes after the trim the previous one queued has run.
        SpriteDiskStore store = NewStore(budgetBytes: 10_000);
        for (int i = 0; i < 40; i++)
        {
            Assert.True(store.TryWrite(Key("bulb" + i), Frame(20))); // about 1,700 bytes each
            await store.Maintenance;
        }

        long total = Directory.EnumerateFiles(store.Root, "*.sprite", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Assert.True(total <= 10_000, $"{total} bytes left");
    }

    [Fact]
    public async Task DiskStore_ConcurrentWritesOfTheSameSpriteAllSucceed()
    {
        // Regression: two writers storing the same key at the same moment (prefetch threads, or the app and a screen-saver
        // process) made File.Move fail with "Access to the path is denied".
        SpriteDiskStore store = NewStore();
        SpriteCacheKey key = Key("shared");
        int failures = 0;

        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            if (!store.TryWrite(key, Frame(64)))
            {
                Interlocked.Increment(ref failures);
            }
        });
        await store.Maintenance;

        Assert.Equal(0, failures);
        Assert.True(store.TryRead(key, out SpriteCacheEntry? read));
        Assert.Equal(new SizeI(64, 64), read.Frame!.Size);
        Assert.Empty(Directory.EnumerateFiles(store.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void DiskStore_ReadsRefreshTheFileTime()
    {
        SpriteDiskStore store = NewStore();
        store.TryWrite(Key("a"), Frame(4));
        File.SetLastWriteTimeUtc(store.GetPath(Key("a")), DateTime.UtcNow.AddDays(-3));

        Assert.True(store.TryRead(Key("a"), out _));

        Assert.True(File.GetLastWriteTimeUtc(store.GetPath(Key("a"))) > DateTime.UtcNow.AddMinutes(-5));
    }

    private static SpriteCacheKey Key(string content, int frame = 0, double scale = 1.5) =>
        SpriteCacheKey.ForFrame(new SpriteKey(content, CellSlot.Top, 0, frame, scale, SpriteStyle.Smooth));

    private static SpriteCacheEntry Frame(int size) => SpriteCacheEntry.ForFrame(new PremultipliedImage(size, size));

    private SpriteDiskStore NewStore(long budgetBytes = 1 << 20) => new(dataRoot.Paths.CacheFolder, budgetBytes, NullAppLog.Instance);
}
