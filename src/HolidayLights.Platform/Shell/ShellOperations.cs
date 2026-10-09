using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HolidayLights.Platform.Files;
using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Shell;

/// <summary>Explorer, settings URIs, Control Panel, Recycle Bin and shortcut helpers (see <see cref="IShellOperations"/>). Owner: platform.</summary>
/// <remarks>
/// Thread-safe: COM work runs in a single-threaded apartment (inline on the UI thread, else on a short-lived STA thread).
/// Holiday Lights works offline (PRODUCT-SPEC 6.11), so only <c>ms-settings:</c> URIs are opened, plus the project's home
/// page when the user follows the link in About. Failures are logged, never shown in a dialog.
/// </remarks>
public sealed class ShellOperations : IShellOperations
{
    private const string LogSource = "Platform.Shell";

    // IFileOperation flags: FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI | FOFX_RECYCLEONDELETE.
    private const uint RecycleFlags = 0x0004 | 0x0010 | 0x0040 | 0x0400 | 0x00080000;

    // IShellLink.Resolve: SLR_NO_UI (1 s timeout in the high word) | SLR_NOUPDATE | SLR_NOSEARCH | SLR_NOTRACK.
    private const uint ResolveFlags = 0x0001 | 0x0004 | 0x0010 | 0x0020 | (1000u << 16);
    private const uint ReadMode = 0;
    private const int MaxPath = 32_768;

    private readonly IAppLog log;

    /// <summary>Creates the service.</summary>
    /// <param name="log">The log.</param>
    public ShellOperations(IAppLog log) => this.log = log;

    /// <inheritdoc />
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Directory.CreateDirectory(path);
            Launch(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true, Verb = "open" }, "open a folder");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            log.Warn(LogSource, "Could not create or open a folder.", e);
        }
    }

    /// <inheritdoc />
    public void ShowInFolder(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string full = Path.GetFullPath(filePath);
        if (!FileSystemEntry.Exists(full))
        {
            OpenFolder(Path.GetDirectoryName(full) ?? full);
            return;
        }

        int hr = ComApartment.Run(() => SelectInExplorer(full));
        if (hr != 0)
        {
            log.Warn(LogSource, $"Explorer could not show the file (0x{hr:X8}); opening its folder.");
            OpenFolder(Path.GetDirectoryName(full) ?? full);
        }
    }

    /// <inheritdoc />
    public void OpenSettingsUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (!uri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            log.Warn(LogSource, "Refused to open a URI that is not a Windows Settings page (Holiday Lights works offline).");
            return;
        }

        Launch(new ProcessStartInfo(uri) { UseShellExecute = true }, $"open {uri}");
    }

    /// <inheritdoc />
    public void OpenProjectHomePage() =>
        Launch(new ProcessStartInfo(ProjectInfo.HomePage) { UseShellExecute = true }, "open the project's home page");

    /// <inheritdoc />
    public void OpenControlPanel(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Launch(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "control.exe"), arguments) { UseShellExecute = false }, $"run control {arguments}");
    }

    /// <inheritdoc />
    public bool MoveToRecycleBin(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!FileSystemEntry.HasRecycleBin(full))
        {
            log.Warn(LogSource, $"{Path.GetFileName(full)} is on a drive without a Recycle Bin; it was not deleted.");
            return false;
        }

        if (!FileSystemEntry.Exists(full))
        {
            return false;
        }

        try
        {
            int hr = ComApartment.Run(() => Recycle(full));
            if (hr != 0 || FileSystemEntry.Exists(full))
            {
                log.Warn(LogSource, $"{Path.GetFileName(full)} could not be sent to the Recycle Bin (0x{hr:X8}).");
                return false;
            }

            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            log.Warn(LogSource, $"{Path.GetFileName(full)} could not be sent to the Recycle Bin.", e);
            return false;
        }
    }

    /// <inheritdoc />
    public string? ResolveShortcut(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        if (!File.Exists(shortcutPath))
        {
            return null;
        }

        try
        {
            return ComApartment.Run(() => ReadShortcutTarget(Path.GetFullPath(shortcutPath)));
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            log.Warn(LogSource, $"The shortcut {Path.GetFileName(shortcutPath)} could not be read.", e);
            return null;
        }
    }

    private static unsafe int SelectInExplorer(string path)
    {
        int hr = NativeMethods.SHParseDisplayName(path, 0, out nint itemList, 0, out _);
        if (hr != 0)
        {
            return hr;
        }

        try
        {
            return NativeMethods.SHOpenFolderAndSelectItems(itemList, 0, null, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(itemList);
        }
    }

    private static int Recycle(string path)
    {
        int hr = NativeMethods.SHCreateItemFromParsingName(path, 0, ShellClassIds.ShellItemInterface, out nint item);
        if (hr != 0)
        {
            return hr;
        }

        IFileOperation operation = ComApartment.Create<IFileOperation>(ShellClassIds.FileOperation);
        try
        {
            hr = operation.SetOperationFlags(RecycleFlags);
            if (hr == 0)
            {
                hr = operation.DeleteItem(item, 0);
            }

            if (hr == 0)
            {
                hr = operation.PerformOperations();
            }

            // E_ABORT (0x80004004) when an item was skipped.
            return hr == 0 && operation.GetAnyOperationsAborted(out bool aborted) == 0 && aborted ? unchecked((int)0x80004004) : hr;
        }
        finally
        {
            ComApartment.Release(operation);
            Marshal.Release(item);
        }
    }

    private static unsafe string? ReadShortcutTarget(string shortcutPath)
    {
        IShellLinkW link = ComApartment.Create<IShellLinkW>(ShellClassIds.ShellLink);
        try
        {
            var file = (IPersistFile)link;
            if (file.Load(shortcutPath, ReadMode) != 0)
            {
                return null;
            }

            // Resolve may fail for a moved target; the stored path is still the best answer.
            link.Resolve(0, ResolveFlags);
            char[] buffer = new char[MaxPath];
            fixed (char* text = buffer)
            {
                return link.GetPath(text, buffer.Length, 0, 0) == 0 && text[0] != '\0' ? new string(text) : null;
            }
        }
        finally
        {
            ComApartment.Release(link);
        }
    }

    private void Launch(ProcessStartInfo start, string description)
    {
        try
        {
            using Process? process = Process.Start(start);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            log.Warn(LogSource, $"Could not {description}.", e);
        }
    }
}
