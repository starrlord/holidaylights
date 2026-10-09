using System.Globalization;
using System.IO;
using System.Windows;
using HolidayLights.App.Controls;
using Microsoft.Win32;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// "Export Bulb File..." (PRODUCT-SPEC 3.2.11): a Save As dialog, then a copy of the <c>.bul</c> written next to the target
/// first and moved into place, so an existing file is never left half-written. A failed copy says why and offers to try
/// another place.
/// </summary>
internal static class BulbExport
{
    private const string TemporaryExtension = ".tmp";

    /// <summary>Asks where to export a bulb file and copies it there.</summary>
    /// <param name="owner">The window the dialogs belong to.</param>
    /// <param name="sourcePath">The bulb's file.</param>
    /// <returns>The exported file, or null when cancelled.</returns>
    public static async Task<string?> ExportAsync(Window owner, string sourcePath)
    {
        while (true)
        {
            var dialog = new SaveFileDialog
            {
                Title = BulbFactoryStrings.ExportDialogTitle,
                Filter = BulbFactoryStrings.BulbFilter,
                FileName = Path.GetFileName(sourcePath),
                DefaultExt = Path.GetExtension(sourcePath),
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(owner) != true)
            {
                return null;
            }

            string target = dialog.FileName;
            try
            {
                await Task.Run(() => Copy(sourcePath, target));
                return target;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                string text = string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.CopyFailedFormat, Path.GetFileName(sourcePath), e.Message);
                var options = new ContentDialogOptions(BulbFactoryStrings.ExportFailedTitle, text, BulbFactoryStrings.TryAgain, BulbFactoryStrings.Cancel, PrimaryIsDestructive: false);
                if (!await ContentDialogs.ShowAsync(owner, options))
                {
                    return null;
                }
            }
        }
    }

    /// <summary>Copies a file through a temporary file in the target folder (nothing to do when source and target are the same file).</summary>
    /// <param name="sourcePath">The file.</param>
    /// <param name="targetPath">The copy.</param>
    internal static void Copy(string sourcePath, string targetPath)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string temporary = targetPath + TemporaryExtension;
        try
        {
            File.Copy(sourcePath, temporary, overwrite: true);
            File.Move(temporary, targetPath, overwrite: true);
        }
        catch
        {
            DeleteQuietly(temporary);
            throw;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the next export to this place overwrites it.
        }
    }
}
