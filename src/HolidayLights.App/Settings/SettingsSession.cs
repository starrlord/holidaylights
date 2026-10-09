using System.IO;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Settings;

/// <summary>
/// One opening of the Settings window and its apply model (PRODUCT-SPEC 2.3): the Cancel snapshot and the Undo history
/// start when the window opens; settings that mirror Windows state ("Automatically Start Holiday Lights", "Open Bulb Files
/// (.bul) with Holiday Lights") are written whenever they change, including by Undo and Cancel; closing commits the
/// holding folder to the Recycle Bin.
/// </summary>
public sealed class SettingsSession : IDisposable
{
    private const string LogSource = "Settings.Window";

    private readonly IAppServices services;
    private bool ended;

    /// <summary>Takes the snapshot and starts the history.</summary>
    /// <param name="services">The services.</param>
    public SettingsSession(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        Snapshot = services.Settings.Current;
        History = new UndoHistory(services.Settings);
        services.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>The settings when the window opened (the Cancel snapshot).</summary>
    public AppSettings Snapshot { get; }

    /// <summary>The Undo/Redo history of this opening.</summary>
    public UndoHistory History { get; }

    /// <summary>
    /// "Cancel": reverts what Cancel reverts (removed files come back, Windows screen saver changes are undone; additions,
    /// saves and renames are kept), then restores the snapshot of every setting in the Cancel scope, made to agree with
    /// what was kept (the calendar uses the new name of a theme renamed in this session).
    /// </summary>
    public void Cancel()
    {
        Func<AppSettings, AppSettings> followKept = History.RevertForCancel();
        foreach (Exception problem in History.CancelProblems)
        {
            services.Log.Warn(LogSource, "Cancel could not bring back a removed file; it goes to the Recycle Bin with the other removed files.", problem);
        }

        services.Settings.Update(s => followKept(CancelScope.Restore(s, Snapshot)), new SettingsChange(SettingsChangeKind.CancelRestore));
    }

    /// <summary>
    /// Ends the session: stops recording and sends the files held in it to the Recycle Bin (on the thread pool, or at once
    /// when the program is exiting).
    /// </summary>
    /// <param name="commitNow">True when the program exits right after (commit on this thread).</param>
    public void End(bool commitNow)
    {
        if (ended)
        {
            return;
        }

        ended = true;
        Dispose();
        if (services.Holding.Items.Count == 0)
        {
            return;
        }

        if (commitNow)
        {
            Commit();
        }
        else
        {
            _ = Task.Run(Commit);
        }
    }

    /// <summary>Stops recording and listening (no commit).</summary>
    public void Dispose()
    {
        History.Dispose();
        services.Settings.Changed -= OnSettingsChanged;
    }

    private void Commit()
    {
        try
        {
            services.Holding.CommitSession();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            services.Log.Warn(LogSource, "The removed files could not be sent to the Recycle Bin; they are recycled at the next start.", exception);
        }
    }

    /// <summary>Keeps Windows in step with the settings that mirror it, whatever changed them (an edit, Undo, Cancel).</summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        try
        {
            if (e.OldSettings.Startup.Auto != e.NewSettings.Startup.Auto)
            {
                if (e.NewSettings.Startup.Auto)
                {
                    services.Startup.Enable();
                }
                else
                {
                    services.Startup.Disable();
                }
            }

            if (e.OldSettings.Files.AssociateBul != e.NewSettings.Files.AssociateBul)
            {
                if (e.NewSettings.Files.AssociateBul)
                {
                    services.FileAssociation.Register();
                }
                else
                {
                    services.FileAssociation.Unregister();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            services.Log.Warn(LogSource, "A Windows setting could not be changed.", exception);
        }
    }
}
