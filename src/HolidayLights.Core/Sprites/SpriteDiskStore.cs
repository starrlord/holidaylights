using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HolidayLights.Core.Sprites;

/// <summary>
/// The disk half of the sprite cache: one small file per sprite or halo under
/// <c>%LOCALAPPDATA%\Holiday Lights\Cache\Sprites\v2\&lt;content&gt;\</c>, where <c>&lt;content&gt;</c> is a hash of the
/// bulb content key (so a bulb's files can be dropped together).
/// </summary>
/// <remarks>
/// <para>File layout (little-endian): magic <c>"HLSP"</c>, format version, kind, has-image flag, two reserved bytes,
/// width, height, offset x, offset y, key length, the canonical key in UTF-8 (compared on read, so a hash collision or a
/// foreign file is a miss), then <c>width x height</c> premultiplied BGRA pixels.</para>
/// <para>Writes are atomic (a temporary file of the writer's own, then replace), so the app and a screen-saver process can
/// share the folder; a writer that meets another one storing the same key retries briefly, and a destination that already
/// holds the same number of bytes counts as stored (a key always encodes to the same bytes). Every disk operation is
/// best-effort: failures are logged once and turn into cache misses. Deleting a bulb's files and trimming the folder to
/// its budget (oldest files first; reads refresh a file's time) run in the background, one task at a time. A trim is
/// queued by the first write of the process and again whenever an eighth of the budget has been written since the last
/// one, so a session that runs all season stays within the budget.</para>
/// </remarks>
internal sealed class SpriteDiskStore
{
    /// <summary>The cache subfolder.</summary>
    public const string FolderName = "Sprites";

    /// <summary>
    /// The file format version; changing it (or the scaling or glow algorithms) moves the cache to a new folder. Version 2:
    /// halos with sigma from the shorter side capped at 24 art pixels and the brightest-channel emissive rule.
    /// </summary>
    public const int FormatVersion = 2;

    private const string LogSource = "Sprites";
    private const string FileExtension = ".sprite";
    private const uint Magic = 0x50534C48; // "HLSP"
    private const int HeaderBytes = 32;
    private const int MaxDimension = 32768;
    private const int MoveAttempts = 4;
    private static readonly TimeSpan StaleTemporaryFileAge = TimeSpan.FromHours(1);

    private readonly IAppLog log;
    private readonly object maintenanceGate = new();
    private readonly long trimIntervalBytes;
    private Task maintenance = Task.CompletedTask;
    private long bytesSinceTrim;
    private int trimQueued;
    private int writeFailureLogged;

    /// <summary>Creates the store; nothing touches the disk until the first read or write.</summary>
    /// <param name="cacheFolder">The cache folder (<see cref="DataPaths.CacheFolder"/>).</param>
    /// <param name="budgetBytes">The most disk space the files may use before the oldest are deleted.</param>
    /// <param name="log">The log.</param>
    public SpriteDiskStore(string cacheFolder, long budgetBytes, IAppLog log)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheFolder);
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        ArgumentNullException.ThrowIfNull(log);
        Root = Path.Combine(cacheFolder, FolderName, "v" + FormatVersion);
        BudgetBytes = budgetBytes;
        this.log = log;
        trimIntervalBytes = Math.Max(1, budgetBytes / 8);
        bytesSinceTrim = trimIntervalBytes; // the first write trims: other versions, stale temporary files, a full folder
    }

    /// <summary>The folder of this format version.</summary>
    public string Root { get; }

    /// <summary>The disk budget.</summary>
    public long BudgetBytes { get; }

    /// <summary>Completes when the background deletions and trimming queued so far are done.</summary>
    public Task Maintenance
    {
        get
        {
            lock (maintenanceGate)
            {
                return maintenance;
            }
        }
    }

    /// <summary>Returns the file of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The full path.</returns>
    public string GetPath(SpriteCacheKey key) => Path.Combine(Root, ContentFolderName(key.ContentKey), key.ToFileName());

    /// <summary>Reads a cached entry.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry (marked persisted) when the file exists and is valid.</param>
    /// <returns>True on a hit.</returns>
    public bool TryRead(SpriteCacheKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SpriteCacheEntry? entry)
    {
        entry = null;
        string path = GetPath(key);
        byte[] data;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            if (stream.Length is < HeaderBytes or > int.MaxValue)
            {
                return false;
            }

            data = new byte[stream.Length];
            stream.ReadExactly(data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        entry = Decode(data, key);
        if (entry is null)
        {
            return false;
        }

        entry.IsPersisted = true;
        TryTouch(path);
        return true;
    }

    /// <summary>Writes an entry atomically and marks it persisted.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>True when the file was written.</returns>
    public bool TryWrite(SpriteCacheKey key, SpriteCacheEntry entry)
    {
        string path = GetPath(key);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        byte[] data = Encode(key, entry);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, data);
            MoveIntoPlace(temporary, path, data.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(temporary);
            if (Interlocked.Exchange(ref writeFailureLogged, 1) == 0)
            {
                log.Info(LogSource, $"Couldn't save a sprite to the cache ({e.GetType().Name}, 0x{e.HResult:X8}); sprites are recomputed when needed.");
            }

            return false;
        }

        entry.IsPersisted = true;
        if (Interlocked.Add(ref bytesSinceTrim, data.Length) >= trimIntervalBytes && Interlocked.CompareExchange(ref trimQueued, 1, 0) == 0)
        {
            Enqueue(Trim);
        }

        return true;
    }

    /// <summary>Queues the deletion of every file of a bulb content key.</summary>
    /// <param name="contentKey">The content key.</param>
    public void DeleteContent(string contentKey)
    {
        string folder = Path.Combine(Root, ContentFolderName(contentKey));
        Enqueue(() =>
        {
            if (Directory.Exists(folder))
            {
                TryDeleteFolder(folder);
            }
        });
    }

    /// <summary>
    /// Trims the cache: deletes folders of other format versions and stale temporary files, then, when the files exceed
    /// the budget, the least recently used files until they fill at most three quarters of it.
    /// </summary>
    public void Prune()
    {
        DeleteOtherVersions();
        var root = new DirectoryInfo(Root);
        if (!root.Exists)
        {
            return;
        }

        var files = new List<FileInfo>();
        long total = 0;
        foreach (FileInfo file in root.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.UtcNow - file.LastWriteTimeUtc > StaleTemporaryFileAge)
                {
                    TryDeleteFile(file.FullName);
                }
            }
            else if (file.Name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
                total += file.Length;
            }
        }

        if (total <= BudgetBytes)
        {
            return;
        }

        long goal = BudgetBytes / 4 * 3;
        int deleted = 0;
        foreach (FileInfo file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total <= goal)
            {
                break;
            }

            if (TryDeleteFile(file.FullName))
            {
                total -= file.Length;
                deleted++;
            }
        }

        log.Info(LogSource, $"Trimmed the sprite cache: deleted {deleted} files.");
    }

    /// <summary>
    /// Moves a written temporary file over the destination. Another writer of the same key (a prefetch thread, or the app
    /// and a screen saver) may be replacing it at the same moment, which Windows reports as access denied or a sharing
    /// violation: the move is retried briefly, and a destination that already has the written length counts as stored,
    /// because a key always encodes to the same bytes.
    /// </summary>
    private static void MoveIntoPlace(string temporary, string path, long length)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (HasLength(path, length))
                {
                    TryDeleteFile(temporary);
                    return;
                }

                if (attempt == MoveAttempts)
                {
                    throw;
                }

                Thread.Sleep(5 * attempt);
            }
        }
    }

    private static bool HasLength(string path, long length)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length == length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The queued trim: writes from now on count towards the next one; this one sees every file written so far.</summary>
    private void Trim()
    {
        Interlocked.Exchange(ref bytesSinceTrim, 0);
        Volatile.Write(ref trimQueued, 0);
        Prune();
    }

    private void DeleteOtherVersions()
    {
        var parent = new DirectoryInfo(Path.GetDirectoryName(Root)!);
        if (!parent.Exists)
        {
            return;
        }

        string current = Path.GetFileName(Root);
        foreach (DirectoryInfo folder in parent.EnumerateDirectories("v*"))
        {
            if (!string.Equals(folder.Name, current, StringComparison.OrdinalIgnoreCase) && int.TryParse(folder.Name.AsSpan(1), out _))
            {
                TryDeleteFolder(folder.FullName);
            }
        }
    }

    private void Enqueue(Action action)
    {
        lock (maintenanceGate)
        {
            maintenance = maintenance.ContinueWith(
                _ => RunMaintenance(action), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void RunMaintenance(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            // Background housekeeping must never fail silently or take anything down; the cache works without it.
            log.Warn(LogSource, "Couldn't clean up the sprite cache.", e);
        }
    }

    /// <summary>The folder name of a content key: 32 hex digits of its SHA-256.</summary>
    /// <param name="contentKey">The content key.</param>
    /// <returns>The folder name.</returns>
    internal static string ContentFolderName(string contentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contentKey)), 0, 16);

    private static byte[] Encode(SpriteCacheKey key, SpriteCacheEntry entry)
    {
        PremultipliedImage? image = entry.Frame ?? entry.Glow?.Image;
        byte[] keyBytes = Encoding.UTF8.GetBytes(key.ToCanonicalString());
        var data = new byte[HeaderBytes + keyBytes.Length + 4L * (image?.Pixels.Length ?? 0)];
        Span<byte> header = data.AsSpan(0, HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        header[8] = (byte)key.Kind;
        header[9] = image is null ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], image?.Width ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], image?.Height ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], entry.Glow?.OffsetX ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], entry.Glow?.OffsetY ?? 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], keyBytes.Length);
        keyBytes.CopyTo(data, HeaderBytes);
        if (image is not null)
        {
            // Pixels are stored as little-endian 32-bit values, the in-memory order of every supported platform.
            MemoryMarshal.AsBytes(image.Pixels.AsSpan()).CopyTo(data.AsSpan(HeaderBytes + keyBytes.Length));
        }

        return data;
    }

    private static SpriteCacheEntry? Decode(byte[] data, SpriteCacheKey key)
    {
        ReadOnlySpan<byte> header = data.AsSpan(0, HeaderBytes);
        int keyLength = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != FormatVersion ||
            header[8] != (byte)key.Kind ||
            keyLength < 0 || keyLength > data.Length - HeaderBytes ||
            Encoding.UTF8.GetString(data, HeaderBytes, keyLength) != key.ToCanonicalString())
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
        int pixelStart = HeaderBytes + keyLength;
        if (header[9] == 0)
        {
            return key.Kind == SpriteKind.Glow && width == 0 && height == 0 && data.Length == pixelStart
                ? SpriteCacheEntry.ForGlow(null)
                : null;
        }

        if (width is < 0 or > MaxDimension || height is < 0 or > MaxDimension || data.Length - pixelStart != 4L * width * height)
        {
            return null;
        }

        uint[] pixels = MemoryMarshal.Cast<byte, uint>(data.AsSpan(pixelStart)).ToArray();
        var image = new PremultipliedImage(width, height, pixels);
        return key.Kind == SpriteKind.Frame
            ? SpriteCacheEntry.ForFrame(image)
            : SpriteCacheEntry.ForGlow(new GlowSprite(
                image, BinaryPrimitives.ReadInt32LittleEndian(header[20..]), BinaryPrimitives.ReadInt32LittleEndian(header[24..])));
    }

    private static void TryTouch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only the trimming order depends on it.
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another process (the screen saver) may be trimming the same cache; what is left goes next time.
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
