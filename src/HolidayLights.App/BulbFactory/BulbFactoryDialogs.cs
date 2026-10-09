using System.Globalization;
using System.IO;
using System.Windows;
using HolidayLights.Core.Bulbs;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// Bulb Editing (980 x 640, slot map, flavors, zoomable faithful preview, the white square, GIF tip, Save/Cancel), Edit
/// Categories, New Category, Bulb Credits, Export Bulb File and GIF-to-bulb creation (see <see cref="IBulbFactoryDialogs"/>;
/// PRODUCT-SPEC 3.3, 3.2.10). Owner: bulb-factory.
/// </summary>
/// <remarks>
/// UI thread, except <see cref="CreateBulbFromGif"/>: it writes a file and waits for the catalog's index, uses only
/// thread-safe services, and may run on the thread pool.
/// </remarks>
public sealed class BulbFactoryDialogs : IBulbFactoryDialogs
{
    private const string LogSource = "BulbFactory";
    private const string GifExtension = ".gif";

    /// <summary>5.4 steps every 10 ticks while the pattern is "Don't Flash" (the Bulb Editing preview still animates).</summary>
    private const int DontFlashInterval = 10;

    private readonly IAppServices services;
    private BulbEditingWindow? editor;

    /// <summary>Creates the dialogs service.</summary>
    /// <param name="services">The application services.</param>
    public BulbFactoryDialogs(IAppServices services) => this.services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public bool HasUnsavedChanges => editor?.ViewModel.HasUnsavedChanges ?? false;

    /// <inheritdoc />
    /// <remarks>
    /// A bulb that is not editable opens Edit Categories instead (5.4). When Bulb Editing is already open it comes to the
    /// front. A file that cannot be read is reloaded in the catalog, which lists it as damaged.
    /// </remarks>
    public async Task<BulbEditingResult> EditBulbAsync(Window owner, string bulbId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(bulbId);
        var unchanged = new BulbEditingResult(false, bulbId, false);
        if (editor is not null)
        {
            editor.Activate();
            return unchanged;
        }

        if (!services.Bulbs.TryGetBulb(bulbId, out IBulb? bulb) || !bulb.IsEditable || bulb.FilePath is not { } path)
        {
            await EditCategoriesAsync(owner, bulbId);
            return unchanged;
        }

        BulbEditorSession session;
        try
        {
            session = await BulbEditorSession.OpenAsync(BulbEditorEnvironment.From(services), bulb.Id, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            services.Log.Warn(LogSource, $"Bulb file {Path.GetFileName(path)} could not be opened for editing.", e);
            await Task.Run(() => services.Bulbs.LoadUserBulb(path));
            return unchanged;
        }

        var window = new BulbEditingWindow(session, services.Bulbs.GetAllCategoryNames(), StepInterval(), ShowMakingBulbsHelp) { Owner = owner };
        editor = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            editor = null;
        }

        return window.ViewModel.Result;
    }

    /// <inheritdoc />
    public Task<bool> EditCategoriesAsync(Window owner, string bulbId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(bulbId);
        if (!services.Bulbs.TryGetInfo(bulbId, out BulbInfo? info))
        {
            return Task.FromResult(false);
        }

        var window = new EditCategoriesWindow(services.Bulbs, info) { Owner = owner };
        window.ShowDialog();
        return Task.FromResult(window.ViewModel.IsSaved);
    }

    /// <inheritdoc />
    public void ShowCredits(Window owner, string bulbId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(bulbId);
        if (services.Bulbs.TryGetBulb(bulbId, out IBulb? bulb))
        {
            new BulbCreditsWindow(new BulbCreditsViewModel(bulb, services.Shell), StepInterval()) { Owner = owner }.ShowDialog();
        }
    }

    /// <inheritdoc />
    public Task<string?> ExportBulbFileAsync(Window owner, string bulbId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrEmpty(bulbId);
        return services.Bulbs.TryGetBulb(bulbId, out IBulb? bulb) && bulb.FilePath is { } path
            ? BulbExport.ExportAsync(owner, path)
            : Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Through <see cref="IBulbCatalog.ImportFile"/>, which writes the bulb with <c>GifBulbFactory</c> and
    /// <c>BulFileWriter</c> and loads it (CONTRACTS 6.6). A file that is not a GIF fails with "Problem Importing File".
    /// </remarks>
    public GifBulbCreation CreateBulbFromGif(string gifPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(gifPath);
        if (!string.Equals(Path.GetExtension(gifPath), GifExtension, StringComparison.OrdinalIgnoreCase))
        {
            return new GifBulbCreation(gifPath, null, null, BulbFactoryStrings.ProblemWithFile, BulbCatalog.UnsupportedFileText);
        }

        BulbImportResult result = services.Bulbs.ImportFile(gifPath);
        if (result is { Outcome: BulbImportOutcome.Added, BulbId: { } id })
        {
            return new GifBulbCreation(gifPath, id, services.Bulbs.TryGetInfo(id, out BulbInfo? info) ? info.FilePath : null, null, null);
        }

        string text = result.Outcome == BulbImportOutcome.Damaged || result.Error is null
            ? BulbCatalog.GifCannotBeImportedText
            : string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.GifNotWrittenFormat, result.Error);
        return new GifBulbCreation(gifPath, null, null, BulbFactoryStrings.CannotImportGif, text);
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmDiscardAsync() => editor is null || await editor.RequestCloseAsync();

    /// <summary>One flash step of the desktop: the effective interval (10 ticks for "Don't Flash", as in 5.4) x 60 ms.</summary>
    private TimeSpan StepInterval()
    {
        LightsScene scene = services.Lights.Scene;
        int ticks = scene.Flash.Pattern == FlashPatternId.DontFlash ? DontFlashInterval : scene.Interval;
        return TimeSpan.FromMilliseconds(ticks * StepClock.TickMilliseconds);
    }

    private void ShowMakingBulbsHelp() => services.AppShell.ShowHelp(HelpTopics.MakingBulbs);
}
