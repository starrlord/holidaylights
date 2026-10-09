namespace HolidayLights.Tests.Settings.Fakes;

/// <summary>
/// A catalog that knows a set of bulb ids, answers content matches from a table and "copies" files into My Bulbs by
/// name (the members the settings code does not use are not supported).
/// </summary>
internal sealed class FakeBulbCatalog : IBulbCatalog
{
    private readonly HashSet<string> known;

    /// <summary>Creates the catalog.</summary>
    /// <param name="knownIds">Ids that resolve (<see cref="TryGetBulb"/>).</param>
    public FakeBulbCatalog(IEnumerable<string>? knownIds = null) => known = new HashSet<string>(knownIds ?? [], BulbIds.Comparer);

    public event EventHandler<BulbCatalogChangedEventArgs>? Changed;

    /// <summary>Content matches: file name (without folder) to the id of the installed bulb with the same content (an imported file matches its copy).</summary>
    public Dictionary<string, string> ContentMatches { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files whose import fails as damaged (file names).</summary>
    public HashSet<string> DamagedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids that imported files get instead of <c>user:&lt;file stem&gt;</c> (file name to id), as when My Bulbs already has that name.</summary>
    public Dictionary<string, string> ImportedIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The files passed to <see cref="ImportFile"/>.</summary>
    public List<string> Imported { get; } = [];

    public BulbCatalogStatus Status => new(0, 0, true);

    public IReadOnlyList<BulbInfo> All => [];

    IReadOnlyList<DamagedBulbFile> IBulbCatalog.DamagedFiles => [];

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb)
    {
        bulb = known.Contains(id) ? new KnownBulb(id) : null;
        return bulb is not null;
    }

    public string? FindByContent(string bulFilePath) => ContentMatches.GetValueOrDefault(Path.GetFileName(bulFilePath));

    public BulbImportResult ImportFile(string sourcePath)
    {
        Imported.Add(sourcePath);
        if (DamagedFiles.Contains(Path.GetFileName(sourcePath)))
        {
            return new BulbImportResult(sourcePath, BulbImportOutcome.Damaged, null, null);
        }

        string id = ImportedIds.GetValueOrDefault(Path.GetFileName(sourcePath)) ?? BulbIds.User(Path.GetFileNameWithoutExtension(sourcePath));
        known.Add(id);
        ContentMatches[Path.GetFileName(sourcePath)] = id;
        Changed?.Invoke(this, new BulbCatalogChangedEventArgs(BulbCatalogChange.Added, [id]));
        return new BulbImportResult(sourcePath, BulbImportOutcome.Added, id, null);
    }

    public bool TryGetInfo(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BulbInfo? info) => throw new NotSupportedException();

    public IReadOnlyList<BulbInfo> Query(BulbQuery query) => throw new NotSupportedException();

    public IReadOnlyList<BulbCategoryCount> GetCategoryCounts() => throw new NotSupportedException();

    public IReadOnlyList<string> GetAllCategoryNames() => throw new NotSupportedException();

    public bool IsFavorite(string id) => throw new NotSupportedException();

    public void SetFavorite(string id, bool favorite) => throw new NotSupportedException();

    public void SetCategoryOverrides(string id, IReadOnlyList<string> categories) => throw new NotSupportedException();

    public void Hide(string id) => throw new NotSupportedException();

    public void Unhide(string id) => throw new NotSupportedException();

    public int AllocateLegacyId() => throw new NotSupportedException();

    public BulbInfo? LoadUserBulb(string filePath) => throw new NotSupportedException();

    public HeldItem RemoveUserBulb(string id) => throw new NotSupportedException();

    public void RestoreUserBulb(HeldItem item) => throw new NotSupportedException();

    /// <summary>A resolvable bulb; the settings code only asks whether it exists.</summary>
    private sealed class KnownBulb(string id) : IBulb
    {
        public string Id => id;

        public int LegacyId => -1;

        public BulbOrigin Origin => BulbIds.TryGetOrigin(id, out BulbOrigin origin) ? origin : BulbOrigin.UserAddOn;

        public string ContentKey => id;

        public string Name => id;

        public string Description => "";

        public string Author => "";

        public string Copyright => "";

        public IReadOnlyList<string> Categories => [];

        public string? FilePath => null;

        public bool IsLocked => true;

        public bool IsEditable => false;

        public bool HasDamagedArt => false;

        public int HorizontalSpacing => 0;

        public int VerticalSpacing => 0;

        public RectI PreviewWindow => default;

        public int GetFlavorCount(Side side) => 1;

        public int GetPhaseCount(Side side) => 1;

        public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor) => throw new NotSupportedException();

        public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => throw new NotSupportedException();

        public BulbCell GetCell(CellSlot slot, int flavor, int phase) => throw new NotSupportedException();
    }
}
