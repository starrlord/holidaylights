using System.Diagnostics;
using System.IO;
using System.Windows;
using HolidayLights.App.About;
using HolidayLights.App.Help;

namespace HolidayLights.App.Shell;

/// <summary>About, Help, Exit and Uninstall (see <see cref="IAppShell"/>). Owner: app-shell.</summary>
public sealed class AppShellCommands : IAppShell
{
    private const string LogSource = "Shell";

    private readonly IAppServices services;
    private readonly Func<Task> shutdown;
    private readonly Func<IBulbFactoryDialogs?> bulbEditing;
    private bool exiting;

    /// <summary>Creates the commands.</summary>
    /// <param name="services">The services.</param>
    public AppShellCommands(IAppServices services)
        : this(services, () => (services as IAsyncDisposable)?.DisposeAsync().AsTask() ?? Task.CompletedTask, () => services?.BulbFactory)
    {
    }

    /// <summary>Creates the commands with the orderly shutdown of the composition root.</summary>
    /// <param name="services">The services.</param>
    /// <param name="shutdown">Fades the lights and the music out, flushes the settings and removes the tray icon.</param>
    /// <param name="bulbEditing">The bulb dialogs when they were ever opened (only then can Bulb Editing hold unsaved changes), else null.</param>
    internal AppShellCommands(IAppServices services, Func<Task> shutdown, Func<IBulbFactoryDialogs?> bulbEditing)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(bulbEditing);
        this.services = services;
        this.shutdown = shutdown;
        this.bulbEditing = bulbEditing;
    }

    /// <inheritdoc />
    public void ShowAbout() => AboutWindowHost.Show(services);

    /// <inheritdoc />
    public void ShowHelp(string? topicId = null) => HelpWindowHost.Show(services, topicId);

    /// <inheritdoc />
    public async Task ExitAsync()
    {
        if (exiting)
        {
            return;
        }

        if (bulbEditing() is { HasUnsavedChanges: true } dialogs && !await dialogs.ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        await ShutdownAsync().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void StartUninstall()
    {
        string uninstaller = services.Paths.InstalledExePath;
        if (!File.Exists(uninstaller))
        {
            services.Log.Warn(LogSource, "The uninstaller was not found.");
            return;
        }

        // Not the program folder as the current folder: the uninstaller removes it, and a process running in a folder keeps it.
        var start = new ProcessStartInfo(uninstaller) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() };
        start.ArgumentList.Add("--uninstall");
        if (services.Paths.DataRoot is { } root)
        {
            start.ArgumentList.Add("--data-root");
            start.ArgumentList.Add(root);
        }

        if (!services.Options.AllowSystemChanges)
        {
            start.ArgumentList.Add("--no-system-changes");
        }

        services.Log.Info(LogSource, "Starting the uninstaller.");
        using (Process.Start(start))
        {
            // The uninstaller waits for this instance to end before it removes the program.
        }

        _ = ShutdownAsync();
    }

    /// <summary>Keeps the Settings changes, then fades out, flushes and ends the program.</summary>
    private async Task ShutdownAsync()
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        services.Log.Info(LogSource, "Exiting Holiday Lights.");
        try
        {
            if (services.SettingsWindow.IsOpen)
            {
                services.SettingsWindow.CloseKeepingChanges();
            }

            await shutdown().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            // "Exit Holiday Lights" always ends the program.
            services.Log.Error(LogSource, "The orderly exit failed.", e);
        }
        finally
        {
            Application.Current?.Shutdown();
        }
    }
}
