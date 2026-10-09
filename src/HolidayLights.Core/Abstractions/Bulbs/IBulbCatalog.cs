using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>
/// Metadata snapshot of one bulb for lists, search and the selected-bulb bar (PRODUCT-SPEC 3.2.5, 3.2.6). Read from the
/// index cache; no pixels are decoded to produce it.
/// </summary>
public sealed record BulbInfo
{
    /// <summary>Stable id (<see cref="BulbIds"/>).</summary>
    public required string Id { get; init; }

    /// <summary>Origin (Built-In, Add-On or My Bulb).</summary>
    public required BulbOrigin Origin { get; init; }

    /// <summary>Name, verbatim.</summary>
    public required string Name { get; init; }

    /// <summary>Description, verbatim.</summary>
    public string Description { get; init; } = "";

    /// <summary>Author field, verbatim.</summary>
    public string Author { get; init; } = "";

    /// <summary>Copyright field, verbatim.</summary>
    public string Copyright { get; init; } = "";

    /// <summary>Effective categories: the user's override (settings <c>bulbs.categoryOverrides</c>) when present, else the bulb's own.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The bulb's own categories (before overrides).</summary>
    public IReadOnlyList<string> OriginalCategories { get; init; } = [];

    /// <summary>Full path of the <c>.bul</c> file; null for built-in bulbs.</summary>
    public string? FilePath { get; init; }

    /// <summary>Last-write time of the file (for "Newest First"); null for built-in bulbs.</summary>
    public DateTimeOffset? FileDate { get; init; }

    /// <summary>When the file was added to My Bulbs (for the "New" pill shown for 24 hours); null otherwise.</summary>
    public DateTimeOffset? AddedDate { get; init; }

    /// <summary>Position in the 5.4 list order: the 49 built-ins in table order, then add-ons A-Z by name ("Original Order").</summary>
    public int OriginalOrder { get; init; }

    /// <summary>Number of top-edge flavors ("5 colors").</summary>
    public int TopFlavorCount { get; init; } = 1;

    /// <summary>Kind of the top-edge animation, flavor 1 ("flashes" / "animated" / "still").</summary>
    public BulbAnimationKind TopKind { get; init; }

    /// <summary>Largest top-edge cell in art pixels ("32 x 32 px").</summary>
    public SizeI LargestTopCell { get; init; }

    /// <summary>True when any cell is larger than 64 px in either direction (the "Big" tag).</summary>
    public bool IsBig { get; init; }

    /// <summary>True when some animation is undecodable and drawn as the WARNING picture.</summary>
    public bool HasDamagedArt { get; init; }

    /// <summary>True when "Edit Bulb..." opens Bulb Editing (My Bulbs file with <c>locked</c> = 0); otherwise "Edit Categories...".</summary>
    public bool IsEditable { get; init; }
}

/// <summary>The "Show" filter of the Bulb List (PRODUCT-SPEC 3.2.5).</summary>
public enum BulbFilter
{
    /// <summary>"All Bulbs": every listed (not removed) bulb.</summary>
    All,

    /// <summary>"Favorites" (settings <c>bulbs.favorites</c>).</summary>
    Favorites,

    /// <summary>"In Use": bulbs in <see cref="BulbQuery.InUseIds"/>.</summary>
    InUse,

    /// <summary>"For &lt;Holiday&gt;": bulbs in any of <see cref="BulbQuery.Categories"/>.</summary>
    Holiday,

    /// <summary>"Built-In Bulbs" (49).</summary>
    BuiltIn,

    /// <summary>"Add-On Bulbs": bundled and My Bulbs files.</summary>
    AddOn,

    /// <summary>"My Bulbs".</summary>
    MyBulbs,

    /// <summary>One category: <see cref="BulbQuery.Categories"/> holds exactly one name.</summary>
    Category,

    /// <summary>"Removed Bulbs": bundled bulbs hidden by the user (settings <c>bulbs.hidden</c>).</summary>
    Removed,
}

/// <summary>Sort order of the Bulb List (persisted as <c>ui.gallery.sort</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BulbSortOrder>))]
public enum BulbSortOrder
{
    /// <summary>"Original Order": the 49 built-ins in 5.4 table order, then add-ons A-Z (the 5.4 list order).</summary>
    [JsonStringEnumMemberName("original")]
    Original,

    /// <summary>"Name", A-Z (current culture, case-insensitive).</summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>"Newest First" by file date; built-ins last.</summary>
    [JsonStringEnumMemberName("newest")]
    Newest,
}

/// <summary>A Bulb List query.</summary>
/// <remarks>
/// With a non-empty <see cref="SearchText"/> the result is in "Best Match" order (name prefix, name word, name substring,
/// category, then description, author or file name; ties by <see cref="Sort"/>). Every search word must match one of
/// name, categories, description, author or file name; matching is case- and accent-insensitive.
/// </remarks>
public sealed record BulbQuery
{
    /// <summary>The Show filter.</summary>
    public BulbFilter Filter { get; init; } = BulbFilter.All;

    /// <summary>Category names for <see cref="BulbFilter.Category"/> (one) and <see cref="BulbFilter.Holiday"/> (several); compared case-insensitively.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Ids of the bulbs on the screen, for <see cref="BulbFilter.InUse"/>.</summary>
    public IReadOnlySet<string>? InUseIds { get; init; }

    /// <summary>Search text (empty = no search).</summary>
    public string SearchText { get; init; } = "";

    /// <summary>Sort order when not searching (tie-break when searching).</summary>
    public BulbSortOrder Sort { get; init; } = BulbSortOrder.Original;

    /// <summary>Only add-on bulbs (bundled and My Bulbs): the "Choose a Bulb" dialog (PRODUCT-SPEC 3.8.4).</summary>
    public bool AddOnsOnly { get; init; }
}

/// <summary>A non-empty category with its number of listed bulbs (the "Show" menu: "Christmas (412)").</summary>
/// <param name="Name">Display name (the most common spelling when names differ only in case).</param>
/// <param name="Count">Number of listed bulbs in the category.</param>
public sealed record BulbCategoryCount(string Name, int Count);

/// <summary>A <c>.bul</c> file that cannot be loaded (5.4 loader rules). Never deleted automatically.</summary>
/// <param name="FilePath">Full path.</param>
/// <param name="Reason">Why it was rejected (for the log and "Show Damaged Bulbs").</param>
public sealed record DamagedBulbFile(string FilePath, string Reason);

/// <summary>Progress of the background index of add-on bulbs (PRODUCT-SPEC 3.2.9).</summary>
/// <param name="IndexedAddOns">Add-on files indexed so far.</param>
/// <param name="TotalAddOns">Add-on files found.</param>
/// <param name="IsComplete">True when every file has been indexed.</param>
public sealed record BulbCatalogStatus(int IndexedAddOns, int TotalAddOns, bool IsComplete);

/// <summary>What changed in the catalog.</summary>
public enum BulbCatalogChange
{
    /// <summary>More add-ons were indexed (see <see cref="IBulbCatalog.Status"/>).</summary>
    IndexProgress,

    /// <summary>Bulbs were added (file copied in, created, or appeared in the watched folder).</summary>
    Added,

    /// <summary>Bulbs were removed from the list (file moved away or hidden).</summary>
    Removed,

    /// <summary>Bulbs changed (file rewritten, categories, favorite or hidden state).</summary>
    Updated,
}

/// <summary>Arguments of <see cref="IBulbCatalog.Changed"/>.</summary>
public sealed class BulbCatalogChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="change">What changed.</param>
    /// <param name="bulbIds">The affected ids (empty for <see cref="BulbCatalogChange.IndexProgress"/>).</param>
    public BulbCatalogChangedEventArgs(BulbCatalogChange change, IReadOnlyList<string> bulbIds)
    {
        Change = change;
        BulbIds = bulbIds;
    }

    /// <summary>What changed.</summary>
    public BulbCatalogChange Change { get; }

    /// <summary>The affected ids.</summary>
    public IReadOnlyList<string> BulbIds { get; }
}

/// <summary>Outcome of adding a <c>.bul</c> file (PRODUCT-SPEC 3.2.10).</summary>
public enum BulbImportOutcome
{
    /// <summary>Copied into My Bulbs (possibly as "&lt;name&gt; (2).bul").</summary>
    Added,

    /// <summary>The same bulb content is already present (6.3 identity); nothing copied; <see cref="BulbImportResult.BulbId"/> is the existing bulb.</summary>
    AlreadyPresent,

    /// <summary>The file is damaged (5.4 loader rules); nothing copied.</summary>
    Damaged,

    /// <summary>The file could not be read or copied (see <see cref="BulbImportResult.Error"/>).</summary>
    Failed,
}

/// <summary>Result of <see cref="IBulbCatalog.ImportFile"/>.</summary>
/// <param name="SourcePath">The file that was added.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="BulbId">The new or existing bulb; null when damaged or failed.</param>
/// <param name="Error">Windows reason for <see cref="BulbImportOutcome.Failed"/>.</param>
public sealed record BulbImportResult(string SourcePath, BulbImportOutcome Outcome, string? BulbId, string? Error);

/// <summary>
/// Every bulb Holiday Lights knows: 49 built-ins, the bundled add-ons (<c>Content\Bulbs</c>) and My Bulbs (watched).
/// Implemented by core-bulbs.
/// </summary>
/// <remarks>
/// <para>Start-up: <see cref="StartAsync"/> makes the built-ins available at once and indexes add-on headers on a
/// background thread, using <c>Cache\index.json</c> (invalidated by file size and date).</para>
/// <para>Settings: the catalog reads favorites, hidden bulbs and category overrides from <see cref="ISettingsStore"/>;
/// the members that change those settings (<see cref="SetFavorite"/>, <see cref="SetCategoryOverrides"/>,
/// <see cref="Hide"/>, <see cref="Unhide"/>) update them through the store (one undoable step each) and must be called
/// on the UI thread.</para>
/// <para>File members: <see cref="ImportFile"/>, <see cref="FindByContent"/> and <see cref="AllocateLegacyId"/> may be
/// called from any thread and block until the add-on index is complete, so call them on the thread pool.</para>
/// <para><see cref="Changed"/> is raised on an arbitrary thread; marshal to the UI thread.</para>
/// </remarks>
public interface IBulbCatalog : IBulbResolver
{
    /// <summary>Indexing progress.</summary>
    BulbCatalogStatus Status { get; }

    /// <summary>Every listed bulb (not hidden, not damaged), in original order. Thread-safe snapshot.</summary>
    IReadOnlyList<BulbInfo> All { get; }

    /// <summary>Files that could not be loaded ("Show Damaged Bulbs").</summary>
    IReadOnlyList<DamagedBulbFile> DamagedFiles { get; }

    /// <summary>Raised when bulbs are indexed, added, removed or updated.</summary>
    event EventHandler<BulbCatalogChangedEventArgs>? Changed;

    /// <summary>Loads the built-ins synchronously, then indexes add-ons in the background and starts watching My Bulbs.</summary>
    /// <param name="cancellationToken">Stops indexing.</param>
    /// <returns>A task that completes when indexing is complete.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the metadata of any known bulb, including hidden ones.</summary>
    /// <param name="id">The bulb id.</param>
    /// <param name="info">The metadata when found.</param>
    /// <returns>True when the bulb is known.</returns>
    bool TryGetInfo(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BulbInfo? info);

    /// <summary>Filters, searches and sorts the Bulb List.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The matching bulbs in display order.</returns>
    IReadOnlyList<BulbInfo> Query(BulbQuery query);

    /// <summary>Non-empty categories of listed bulbs, merged case-insensitively, A-Z (the Show menu).</summary>
    /// <returns>Categories with counts.</returns>
    IReadOnlyList<BulbCategoryCount> GetCategoryCounts();

    /// <summary>Every known category name (built-in, bundled, user and override categories), A-Z, for the category check lists.</summary>
    /// <returns>Category names.</returns>
    IReadOnlyList<string> GetAllCategoryNames();

    /// <summary>True when the bulb is a favorite (settings <c>bulbs.favorites</c>).</summary>
    /// <param name="id">The bulb id.</param>
    /// <returns>True for a favorite.</returns>
    bool IsFavorite(string id);

    /// <summary>Adds or removes a favorite (settings update; UI thread).</summary>
    /// <param name="id">The bulb id.</param>
    /// <param name="favorite">The new state.</param>
    void SetFavorite(string id, bool favorite);

    /// <summary>Stores the categories of a built-in or someone else's bulb in settings (<c>bulbs.categoryOverrides</c>); the file is never touched (UI thread).</summary>
    /// <param name="id">The bulb id.</param>
    /// <param name="categories">The new categories; the bulb's own categories remove the override.</param>
    void SetCategoryOverrides(string id, IReadOnlyList<string> categories);

    /// <summary>Hides a bundled add-on bulb ("Remove Bulb" on a bundled bulb; settings <c>bulbs.hidden</c>; UI thread). Arrangements and themes keep it.</summary>
    /// <param name="id">An <c>addon:</c> id.</param>
    void Hide(string id);

    /// <summary>Lists a hidden bundled bulb again ("Restore Bulb"; UI thread).</summary>
    /// <param name="id">An <c>addon:</c> id.</param>
    void Unhide(string id);

    /// <summary>
    /// Adds a <c>.bul</c> file (equal content detected per PRODUCT-SPEC 6.3, otherwise copied into My Bulbs with a unique
    /// name) or turns a <c>.gif</c> into a new My Bulbs bulb with the 5.4 defaults through the Bulb Factory writer
    /// (outcome <see cref="BulbImportOutcome.Damaged"/> = "Cannot Import GIF File"); any other file type fails.
    /// </summary>
    /// <param name="sourcePath">The file to add.</param>
    /// <returns>What happened.</returns>
    /// <remarks>Thread-safe; waits for the add-on index (call it on the thread pool).</remarks>
    BulbImportResult ImportFile(string sourcePath);

    /// <summary>Finds a loaded bulb whose content equals a <c>.bul</c> file (6.3 identity; used by the 5.4 import).</summary>
    /// <param name="bulFilePath">The file to compare.</param>
    /// <returns>The id of the equal bulb, or null.</returns>
    /// <remarks>Thread-safe; waits for the add-on index (call it on the thread pool).</remarks>
    string? FindByContent(string bulFilePath);

    /// <summary>
    /// Returns a header bulb id (<c>.bul</c> offset 0x0C) that no known bulb uses and that the catalog has not handed out
    /// before: "a fresh bulb id not used by any loaded bulb" (PRODUCT-SPEC 6.3) for <c>BulFileWriter.Encode</c> (Bulb
    /// Editing, GIF to bulb). Waits for indexing to finish.
    /// </summary>
    /// <returns>The id (49 or more).</returns>
    int AllocateLegacyId();

    /// <summary>Loads or reloads one file of My Bulbs synchronously (after Bulb Editing saved it or a GIF became a bulb), so it can be selected at once.</summary>
    /// <param name="filePath">A file inside the My Bulbs folder.</param>
    /// <returns>The bulb's metadata, or null when the file is damaged.</returns>
    BulbInfo? LoadUserBulb(string filePath);

    /// <summary>Moves a My Bulbs file to the holding folder (undoable "Remove Bulb").</summary>
    /// <param name="id">A <c>user:</c> id.</param>
    /// <returns>The held file, for undo.</returns>
    HeldItem RemoveUserBulb(string id);

    /// <summary>Brings back a My Bulbs file from the holding folder (undo of a removal).</summary>
    /// <param name="item">The held file returned by <see cref="RemoveUserBulb"/>.</param>
    void RestoreUserBulb(HeldItem item);
}
