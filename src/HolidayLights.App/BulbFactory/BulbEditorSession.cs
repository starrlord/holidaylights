using System.Globalization;
using System.IO;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>The services Bulb Editing uses (a seam for tests).</summary>
/// <param name="Catalog">The bulb catalog (header ids, reloading the saved file).</param>
/// <param name="Holding">The holding folder that keeps the previous file for Undo.</param>
/// <param name="Undo">The Settings window's Undo history at the time of a save.</param>
/// <param name="Log">The log.</param>
internal sealed record BulbEditorEnvironment(IBulbCatalog Catalog, IHoldingFolder Holding, Func<IUndoHistory> Undo, IAppLog Log)
{
    /// <summary>The environment of the running application.</summary>
    /// <param name="services">The application services.</param>
    /// <returns>The environment.</returns>
    public static BulbEditorEnvironment From(IAppServices services) => new(services.Bulbs, services.Holding, () => services.Undo, services.Log);
}

/// <summary>
/// The file side of Bulb Editing (PRODUCT-SPEC 3.3.1 "Save"): opens a My Bulbs file, and saves a document as a new,
/// compacted, 5.4-compatible <c>.bul</c> under the same file name: written to <c>&lt;name&gt;.bul.tmp</c>, the previous file
/// moved to the holding folder, the new one moved into place, the bulb reloaded in the catalog (the lights follow), and one
/// Undo step recorded in Settings that swaps the two versions.
/// </summary>
internal sealed class BulbEditorSession
{
    private const string LogSource = "BulbFactory.Editing";

    private readonly BulbEditorEnvironment environment;

    private BulbEditorSession(BulbEditorEnvironment environment, string bulbId, string filePath, BulbDocument original)
    {
        this.environment = environment;
        BulbId = bulbId;
        FilePath = filePath;
        Original = original;
    }

    /// <summary>The bulb (<c>user:</c> id; it keeps its file name when renamed).</summary>
    public string BulbId { get; }

    /// <summary>The bulb's file in My Bulbs.</summary>
    public string FilePath { get; }

    /// <summary>The bulb as it was opened (do not change it).</summary>
    public BulbDocument Original { get; }

    /// <summary>The catalog, for the edge sample's other bulbs.</summary>
    public IBulbCatalog Catalog => environment.Catalog;

    /// <summary>Reads the bulb's file on the thread pool.</summary>
    /// <param name="environment">The services.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <param name="filePath">Its file.</param>
    /// <returns>The session.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file is damaged.</exception>
    public static async Task<BulbEditorSession> OpenAsync(BulbEditorEnvironment environment, string bulbId, string filePath)
    {
        BulFile file = await Task.Run(() => BulFile.Read(filePath));
        if (file.IsDamaged)
        {
            throw new InvalidDataException(file.Problems[0]);
        }

        return new BulbEditorSession(environment, bulbId, filePath, BulbDocument.FromFile(file));
    }

    /// <summary>Saves a document over the bulb's file and records the Undo step.</summary>
    /// <param name="document">A complete document with a name.</param>
    /// <returns>True when characters had to be replaced by "?".</returns>
    /// <exception cref="IOException">The file cannot be written; the previous file is still in place.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable; the previous file is still in place.</exception>
    /// <exception cref="InvalidOperationException">The bulb would be larger than a bulb file can be.</exception>
    public async Task<bool> SaveAsync(BulbDocument document)
    {
        BulbDocument copy = document.Clone();
        BulFileContent content = await Task.Run(() => BulFileWriter.Encode(copy, environment.Catalog.AllocateLegacyId()));
        HeldItem previous = await Task.Run(() => Replace(content));
        await Task.Run(() => environment.Catalog.LoadUserBulb(FilePath));
        environment.Log.Info(LogSource, $"Saved bulb file {Path.GetFileName(FilePath)}.");
        var versions = new SavedVersions(this, previous);
        environment.Undo().Record(new UndoStep(
            string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.EditUndoFormat, copy.Name.Trim()),
            versions.Swap,
            versions.Swap));
        return content.CharactersReplaced;
    }

    /// <summary>Writes the temporary file, holds the previous file and moves the new one into place (rolling back on failure).</summary>
    private HeldItem Replace(BulFileContent content)
    {
        string temporary = BulFileWriter.WriteTemporaryFile(FilePath, content);
        HeldItem previous;
        try
        {
            previous = environment.Holding.Hold(FilePath);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }

        try
        {
            File.Move(temporary, FilePath);
        }
        catch
        {
            environment.Holding.Restore(previous);
            DeleteQuietly(temporary);
            throw;
        }

        return previous;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover .tmp is overwritten by the next save of this bulb.
        }
    }

    /// <summary>The version of a saved bulb that is not in place; Undo and Redo swap it with the one that is.</summary>
    /// <param name="session">The session that saved the bulb.</param>
    /// <param name="previous">The file as it was before the save, in the holding folder.</param>
    private sealed class SavedVersions(BulbEditorSession session, HeldItem previous)
    {
        private HeldItem held = previous;

        /// <summary>Moves the file in place to the holding folder and brings back the other version (UI thread).</summary>
        public void Swap()
        {
            IHoldingFolder holding = session.environment.Holding;
            try
            {
                HeldItem current = holding.Hold(session.FilePath);
                string restored;
                try
                {
                    restored = holding.Restore(held);
                }
                catch
                {
                    holding.Restore(current);
                    throw;
                }

                held = current;
                session.environment.Catalog.LoadUserBulb(restored);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                session.environment.Log.Warn(LogSource, $"Couldn't swap the saved versions of {Path.GetFileName(session.FilePath)}.", e);
            }
        }
    }
}
