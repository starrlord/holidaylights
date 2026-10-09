using System.Diagnostics.CodeAnalysis;

namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>
/// A catalog frozen in the middle of a first start (PRODUCT-SPEC 3.2.9): the built-in bulbs are there, the add-ons are
/// still being indexed ("640 of 1,501"). Everything else is the real catalog's.
/// </summary>
/// <param name="inner">The real catalog.</param>
public sealed class LoadingCatalog(IBulbCatalog inner) : IBulbCatalog
{
    /// <inheritdoc />
    public event EventHandler<BulbCatalogChangedEventArgs>? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    /// <inheritdoc />
    public BulbCatalogStatus Status { get; } = new(640, 1501, false);

    /// <inheritdoc />
    public IReadOnlyList<BulbInfo> All => [.. inner.All.Where(b => b.Origin == BulbOrigin.BuiltIn)];

    /// <inheritdoc />
    public IReadOnlyList<DamagedBulbFile> DamagedFiles => [];

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public bool TryGetBulb(string id, [NotNullWhen(true)] out IBulb? bulb) => inner.TryGetBulb(id, out bulb);

    /// <inheritdoc />
    public bool TryGetInfo(string id, [NotNullWhen(true)] out BulbInfo? info) => inner.TryGetInfo(id, out info);

    /// <inheritdoc />
    public IReadOnlyList<BulbInfo> Query(BulbQuery query) => [.. inner.Query(query).Where(b => b.Origin == BulbOrigin.BuiltIn)];

    /// <inheritdoc />
    public IReadOnlyList<BulbCategoryCount> GetCategoryCounts() => inner.GetCategoryCounts();

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllCategoryNames() => inner.GetAllCategoryNames();

    /// <inheritdoc />
    public bool IsFavorite(string id) => inner.IsFavorite(id);

    /// <inheritdoc />
    public void SetFavorite(string id, bool favorite) => inner.SetFavorite(id, favorite);

    /// <inheritdoc />
    public void SetCategoryOverrides(string id, IReadOnlyList<string> categories) => inner.SetCategoryOverrides(id, categories);

    /// <inheritdoc />
    public void Hide(string id) => inner.Hide(id);

    /// <inheritdoc />
    public void Unhide(string id) => inner.Unhide(id);

    /// <inheritdoc />
    public BulbImportResult ImportFile(string sourcePath) => inner.ImportFile(sourcePath);

    /// <inheritdoc />
    public string? FindByContent(string bulFilePath) => inner.FindByContent(bulFilePath);

    /// <inheritdoc />
    public int AllocateLegacyId() => inner.AllocateLegacyId();

    /// <inheritdoc />
    public BulbInfo? LoadUserBulb(string filePath) => inner.LoadUserBulb(filePath);

    /// <inheritdoc />
    public HeldItem RemoveUserBulb(string id) => inner.RemoveUserBulb(id);

    /// <inheritdoc />
    public void RestoreUserBulb(HeldItem item) => inner.RestoreUserBulb(item);
}
