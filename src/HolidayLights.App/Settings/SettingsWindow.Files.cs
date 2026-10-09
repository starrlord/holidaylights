using System.Windows;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings;

/// <summary>Files dropped anywhere on the window (PRODUCT-SPEC 3.2.7).</summary>
public partial class SettingsWindow
{
    /// <inheritdoc />
    public void AddFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string[] bulbs = [.. paths.Where(DroppedFiles.IsBulbFile)];
        string[] songs = [.. paths.Where(DroppedFiles.IsSong)];
        string[] pictures = [.. paths.Where(DroppedFiles.IsPicture)];
        if (paths.Count > bulbs.Length + songs.Length + pictures.Length)
        {
            ShowWindowMessage(DroppedFiles.UnsupportedText, InfoBarSeverity.Warning);
        }

        if (songs.Length > 0
            && MediaImport.Problem(MediaImport.AddSongs(services, this, songs), name => $"{name} is already in your Music Box.") is { } songProblem)
        {
            ShowWindowMessage(songProblem, InfoBarSeverity.Warning);
        }

        if (pictures.Length > 0
            && MediaImport.Problem(MediaImport.AddPictures(services, this, pictures), name => $"{name} is already in your pictures.") is { } pictureProblem)
        {
            ShowWindowMessage(pictureProblem, InfoBarSeverity.Warning);
        }

        if (bulbs.Length > 0)
        {
            HandleRequest(SettingsPageId.BulbFactory, new OpenFilesRequest(bulbs));
        }
    }

    /// <summary>Shows a sentence in the InfoBar above the page.</summary>
    /// <param name="text">The sentence.</param>
    /// <param name="severity">The severity.</param>
    public void ShowWindowMessage(string text, InfoBarSeverity severity)
    {
        WindowInfoBar.Severity = severity;
        WindowInfoBar.Message = text;
        WindowInfoBar.IsOpen = true;
    }

    /// <inheritdoc />
    void ISettingsHost.ShowMessage(string text, InfoBarSeverity severity) => ShowWindowMessage(text, severity);

    /// <summary>"Couldn't save your settings: &lt;Windows reason&gt;." (Appendix D).</summary>
    private void OnSaveFailed(object? sender, SettingsSaveFailedEventArgs e) =>
        Dispatcher.BeginInvoke(() => ShowWindowMessage($"Couldn't save your settings: {e.Exception.Message}", InfoBarSeverity.Error));

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !ContentDialogs.GetIsDialogOpen(this) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Handled || ContentDialogs.GetIsDialogOpen(this) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        e.Handled = true;
        Activate();
        AddFiles(paths);
    }
}
