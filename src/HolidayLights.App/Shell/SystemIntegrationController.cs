using System.IO;

namespace HolidayLights.App.Shell;

/// <summary>
/// Keeps Windows in step with two settings (PRODUCT-SPEC 6.6.1, 6.9): "Automatically Start Holiday Lights" (the per-user
/// Run value, applied at once and re-pointed at this installation at start) and "Open Bulb Files (.bul) with Holiday
/// Lights" (the per-user association, written when turned on, removed when turned off, repaired at start only when missing
/// or broken, never taken from another program). Writes run on the thread pool; the platform services skip them under
/// <c>--no-system-changes</c>. UI thread for the settings events.
/// </summary>
public sealed class SystemIntegrationController : IDisposable
{
    private const string LogSource = "Shell.Integration";

    private readonly ISettingsStore settings;
    private readonly IStartupRegistration startup;
    private readonly IFileAssociation association;
    private readonly IAppLog log;
    private bool started;

    /// <summary>Creates the controller; call <see cref="Start"/>.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="startup">The Run value.</param>
    /// <param name="association">The <c>.bul</c> association.</param>
    /// <param name="log">The log.</param>
    public SystemIntegrationController(ISettingsStore settings, IStartupRegistration startup, IFileAssociation association, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(log);
        this.settings = settings;
        this.startup = startup;
        this.association = association;
        this.log = log;
    }

    /// <summary>Starts following the settings.</summary>
    /// <param name="reconcile">True in the normal app: bring the Run value and the association in line with the settings now.</param>
    /// <returns>A task that completes when the start-up reconciliation is done.</returns>
    public Task Start(bool reconcile)
    {
        if (started)
        {
            return Task.CompletedTask;
        }

        started = true;
        settings.Changed += OnSettingsChanged;
        if (!reconcile)
        {
            return Task.CompletedTask;
        }

        AppSettings current = settings.Current;
        return Task.Run(() =>
        {
            ApplyStartup(current.Startup.Auto, onlyWhenDifferent: true);
            if (current.Files.AssociateBul)
            {
                RepairAssociation();
            }
        });
    }

    /// <summary>Stops following the settings.</summary>
    public void Dispose()
    {
        if (started)
        {
            settings.Changed -= OnSettingsChanged;
        }
    }

    /// <summary>Writes or removes the Run value.</summary>
    /// <param name="auto">"Automatically Start Holiday Lights".</param>
    /// <param name="onlyWhenDifferent">Leave Windows alone when it already matches.</param>
    internal void ApplyStartup(bool auto, bool onlyWhenDifferent)
    {
        try
        {
            if (onlyWhenDifferent && startup.IsEnabled == auto)
            {
                return;
            }

            if (auto)
            {
                startup.Enable();
            }
            else
            {
                startup.Disable();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log.Warn(LogSource, "The startup setting could not be applied.", e);
        }
    }

    /// <summary>Writes the association when it is missing or points at a missing program; another program's association is kept.</summary>
    internal void RepairAssociation()
    {
        try
        {
            FileAssociationState state = association.GetState();
            if (state is FileAssociationState.NotRegistered or FileAssociationState.Broken)
            {
                log.Info(LogSource, $"Repairing the .bul association ({state}).");
                association.Register();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log.Warn(LogSource, "The .bul association could not be repaired.", e);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        bool auto = e.NewSettings.Startup.Auto;
        if (e.OldSettings.Startup.Auto != auto)
        {
            _ = Task.Run(() => ApplyStartup(auto, onlyWhenDifferent: false));
        }

        bool associate = e.NewSettings.Files.AssociateBul;
        if (e.OldSettings.Files.AssociateBul != associate)
        {
            _ = Task.Run(() => ApplyAssociation(associate));
        }
    }

    private void ApplyAssociation(bool associate)
    {
        try
        {
            if (associate)
            {
                if (association.GetState() != FileAssociationState.OwnedByAnotherProgram)
                {
                    association.Register();
                }
            }
            else
            {
                association.Unregister();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log.Warn(LogSource, "The .bul association could not be changed.", e);
        }
    }
}
