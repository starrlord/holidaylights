using HolidayLights.Core.Bulbs.Catalog;

namespace HolidayLights.Core.Bulbs;

/// <summary>Adding, reloading, removing and restoring bulb files.</summary>
public sealed partial class BulbCatalog
{
    /// <summary>The 5.4 text for an undecodable GIF ("Cannot Import GIF File").</summary>
    public const string GifCannotBeImportedText = "Sorry, this GIF file can't be imported. It may be damaged in some way.";

    /// <summary>The text for a file that is neither a bulb nor a GIF ("Problem Importing File").</summary>
    public const string UnsupportedFileText =
        "Sorry, that type of file can't be added. Holiday Lights can add bulb files (.bul) and animated GIF pictures (.gif).";

    /// <summary>Built-in bulbs hold the 5.4 ids 0-48; a new bulb file never gets one of them.</summary>
    private const int FirstAddOnLegacyId = 49;

    private const int MaxCopyNameAttempts = 10000;

    private readonly HashSet<int> allocatedLegacyIds = [];

    /// <inheritdoc />
    /// <remarks>
    /// Besides <c>.bul</c> files this also turns a <c>.gif</c> into a new bulb with the 5.4 defaults (through the Bulb
    /// Factory writer, <see cref="Writing.GifBulbFactory"/>); the result is then <see cref="BulbImportOutcome.Added"/>,
    /// <see cref="BulbImportOutcome.Damaged"/> (undecodable GIF: show "Cannot Import GIF File" with
    /// <see cref="GifCannotBeImportedText"/>) or <see cref="BulbImportOutcome.Failed"/>. Any other file type fails with
    /// <see cref="UnsupportedFileText"/>. Content identity covers every known bulb, hidden ones included, so this waits
    /// for indexing to finish.
    /// </remarks>
    public BulbImportResult ImportFile(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ObjectDisposedException.ThrowIf(disposed, this);
        string extension = Path.GetExtension(sourcePath);
        if (extension.Equals(BulExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ImportBulFile(sourcePath);
        }

        return extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            ? ImportGif(sourcePath)
            : new BulbImportResult(sourcePath, BulbImportOutcome.Failed, null, UnsupportedFileText);
    }

    /// <inheritdoc />
    public string? FindByContent(string bulFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(bulFilePath);
        BulFile file;
        try
        {
            file = BulFile.Read(bulFilePath);
        }
        catch (IOException)
        {
            return null;
        }

        if (file.IsDamaged)
        {
            return null;
        }

        WaitForIndex();
        return Current.FindByIdentity(file.ComputeContentIdentity());
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is not directly inside the My Bulbs folder.</exception>
    public BulbInfo? LoadUserBulb(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ObjectDisposedException.ThrowIf(disposed, this);
        string path = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(paths.MyBulbsFolder), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file is not in the My Bulbs folder.", nameof(filePath));
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            CommitFileChanges([], [path]);
            return null;
        }

        IndexedFile file = LoadFile(BulbOrigin.UserAddOn, FileStamp.Of(info));
        CommitFileChanges([file], []);
        return file.Entry is not null && Current.TryGetInfo(file.Id, out BulbInfo? loaded) ? loaded : null;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a <c>user:</c> id.</exception>
    /// <exception cref="IOException">The file cannot be moved.</exception>
    public HeldItem RemoveUserBulb(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!BulbIds.TryGetOrigin(id, out BulbOrigin origin) || origin != BulbOrigin.UserAddOn)
        {
            throw new ArgumentException($"'{id}' is not a My Bulbs id; bundled bulbs are hidden instead.", nameof(id));
        }

        string path;
        lock (gate)
        {
            path = entries.TryGetValue(id, out CatalogEntry? entry) && entry.FilePath is not null
                ? entry.FilePath
                : ConventionalPath(origin, id) ?? throw new ArgumentException($"'{id}' does not name a file.", nameof(id));
        }

        HeldItem held = holding.Hold(path);
        CommitFileChanges([], [path]);
        return held;
    }

    /// <inheritdoc />
    public void RestoreUserBulb(HeldItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ObjectDisposedException.ThrowIf(disposed, this);
        LoadUserBulb(holding.Restore(item));
    }

    /// <summary>
    /// Returns a header bulb id that no known bulb uses and that this catalog has not handed out before: what a new
    /// <c>.bul</c> file is written with ("a fresh bulb id not used by any loaded bulb", PRODUCT-SPEC 6.3). Like 5.4's
    /// <c>GetTickCount()</c> ids it starts from the system tick count, then counts up past used ids and the built-in ids 0-48.
    /// </summary>
    /// <returns>The id.</returns>
    public int AllocateLegacyId()
    {
        WaitForIndex();
        lock (gate)
        {
            var used = new HashSet<int>(entries.Values.Select(e => e.LegacyId));
            used.UnionWith(allocatedLegacyIds);
            int id = (int)(Environment.TickCount64 & int.MaxValue);
            while (id < FirstAddOnLegacyId || used.Contains(id))
            {
                id = id == int.MaxValue ? FirstAddOnLegacyId : id + 1;
            }

            allocatedLegacyIds.Add(id);
            return id;
        }
    }

    private BulbImportResult ImportBulFile(string sourcePath)
    {
        BulFile file;
        try
        {
            file = BulFile.Read(sourcePath);
        }
        catch (IOException e)
        {
            return new BulbImportResult(sourcePath, BulbImportOutcome.Failed, null, e.Message);
        }

        if (file.IsDamaged)
        {
            log.Warn(LogSource, $"Bulb file {Path.GetFileName(sourcePath)} was not added because it is damaged: {string.Join(" ", file.Problems)}");
            return new BulbImportResult(sourcePath, BulbImportOutcome.Damaged, null, null);
        }

        WaitForIndex();
        if (Current.FindByIdentity(file.ComputeContentIdentity()) is { } existing)
        {
            return new BulbImportResult(sourcePath, BulbImportOutcome.AlreadyPresent, existing, null);
        }

        string target;
        try
        {
            target = CopyIntoMyBulbs(sourcePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new BulbImportResult(sourcePath, BulbImportOutcome.Failed, null, e.Message);
        }

        log.Info(LogSource, $"Added bulb file {Path.GetFileName(target)}.");
        return LoadUserBulb(target) is { } added
            ? new BulbImportResult(sourcePath, BulbImportOutcome.Added, added.Id, null)
            : new BulbImportResult(sourcePath, BulbImportOutcome.Damaged, null, null);
    }

    private BulbImportResult ImportGif(string gifPath)
    {
        int bulbId = AllocateLegacyId();
        string path;
        try
        {
            string folder = DataPaths.EnsureFolder(paths.MyBulbsFolder);
            path = gifWriter.Write(gifPath, folder, authorName(), time.GetLocalNow().Year, bulbId);
        }
        catch (InvalidDataException)
        {
            return new BulbImportResult(gifPath, BulbImportOutcome.Damaged, null, GifCannotBeImportedText);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new BulbImportResult(gifPath, BulbImportOutcome.Failed, null, e.Message);
        }

        log.Info(LogSource, $"Made bulb file {Path.GetFileName(path)} from a GIF.");
        return LoadUserBulb(path) is { } added
            ? new BulbImportResult(gifPath, BulbImportOutcome.Added, added.Id, null)
            : new BulbImportResult(gifPath, BulbImportOutcome.Damaged, null, GifCannotBeImportedText);
    }

    /// <summary>
    /// Copies a file into My Bulbs as <c>&lt;name&gt;.bul</c>, or <c>&lt;name&gt; (2).bul</c>, ... when that name is taken
    /// (PRODUCT-SPEC 3.2.10): first to a temporary name, then renamed, so the watcher never sees half a file. The copy's
    /// creation time is now (the Bulb List shows "New" for 24 hours after it).
    /// </summary>
    private string CopyIntoMyBulbs(string sourcePath)
    {
        string folder = DataPaths.EnsureFolder(paths.MyBulbsFolder);
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        for (int copy = 1; copy <= MaxCopyNameAttempts; copy++)
        {
            string target = Path.Combine(folder, (copy == 1 ? stem : $"{stem} ({copy})") + BulExtension);
            if (File.Exists(target))
            {
                continue;
            }

            string temporary = Path.Combine(folder, $"{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
            File.Copy(sourcePath, temporary);
            try
            {
                File.Move(temporary, target);
            }
            catch (IOException) when (File.Exists(target))
            {
                File.Delete(temporary);
                continue;
            }

            File.SetCreationTimeUtc(target, time.GetUtcNow().UtcDateTime);
            return target;
        }

        throw new IOException($"No free file name for \"{stem}\" in the My Bulbs folder.");
    }
}
