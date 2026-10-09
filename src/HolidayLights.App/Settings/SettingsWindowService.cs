using System.Windows;
using HolidayLights.App.Settings.Undo;
using HolidayLights.App.Styles;

namespace HolidayLights.App.Settings;

/// <summary>
/// Opens the single Settings window and implements its apply model (snapshot, Cancel, Undo/Redo, holding-folder commit,
/// first-close notification); see <see cref="ISettingsWindowService"/>. Owner: settings-ui.
/// </summary>
public sealed class SettingsWindowService : ISettingsWindowService
{
    private const string FirstCloseTitle = "Your lights stay on";
    private const string FirstCloseText = "Holiday Lights keeps running in the notification area. Click the red bulb to come back.";

    private readonly IAppServices services;
    private SettingsWindow? window;
    private bool exiting;

    /// <summary>Creates the service (no window yet).</summary>
    /// <param name="services">The application services.</param>
    public SettingsWindowService(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        if (Application.Current is { } application)
        {
            HighContrastResources.Attach(application.Resources);
        }
    }

    /// <inheritdoc />
    public event EventHandler? Opened;

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <inheritdoc />
    public bool IsOpen => window is not null;

    /// <inheritdoc />
    public Window? Window => window;

    /// <inheritdoc />
    public IUndoHistory Undo => window is null ? NullUndoHistory.Instance : window.Session.History;

    /// <inheritdoc />
    public SettingsPageId? CurrentPage => window?.CurrentPage;

    /// <summary>The page a request belongs to.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The page that handles it.</returns>
    public static SettingsPageId PageFor(SettingsRequest request) => request switch
    {
        OpenFilesRequest => SettingsPageId.BulbFactory,
        ThemeSwitchedRequest => SettingsPageId.Themes,
        ResetRequest or ImportDetailsRequest => SettingsPageId.General,
        _ => SettingsPageId.Home,
    };

    /// <inheritdoc />
    public void Show(SettingsPageId? page = null, SettingsRequest? request = null)
    {
        SettingsPageId target = page ?? (request is null ? services.Settings.Current.Ui.Settings.LastPage : PageFor(request));
        if (window is null)
        {
            Open(target);
        }
        else
        {
            window.ShowPage(target);
            BringForward(window);
        }

        if (request is not null)
        {
            window!.HandleRequest(PageFor(request), request);
        }
    }

    /// <inheritdoc />
    public void CloseKeepingChanges()
    {
        if (window is null)
        {
            return;
        }

        exiting = true;
        window.CloseKeepingChanges();
    }

    private static void BringForward(Window existing)
    {
        if (existing.WindowState == WindowState.Minimized)
        {
            existing.WindowState = WindowState.Normal;
        }

        existing.Show();
        existing.Activate();
    }

    private void Open(SettingsPageId page)
    {
        var created = new SettingsWindow(services);
        created.Closed += OnWindowClosed;
        window = created;
        WindowPlacementPlan plan = WindowPlacement.Plan(services.Settings.Current.Ui.Settings.Window, services.Displays.Displays, services.Displays.FromCursor());
        WindowPlacement.Apply(created, plan, services.Displays.Primary.Scale);
        created.ShowPage(page);
        created.Show();
        created.Activate();
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        window = null;
        bool trim = !exiting;
        if (!exiting)
        {
            ShowFirstCloseNotification();
        }

        exiting = false;
        Closed?.Invoke(this, EventArgs.Empty);
        if (trim && sender is Window closed)
        {
            // Once the window is gone (and unless Settings opened again meanwhile), give the preview caches back (review r1 #19).
            closed.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                if (window is null)
                {
                    MemoryTrim.AfterSettingsClosed(services);
                }
            });
        }
    }

    /// <summary>
    /// The first close ever: "Your lights stay on" (only where a tray icon exists, the normal session, and never when
    /// "Exit Holiday Lights" closes the window, since the lights do not stay on then).
    /// </summary>
    private void ShowFirstCloseNotification()
    {
        if (services.Settings.Current.Onboarding.CloseNotified || services.Options.Session != AppSessionKind.Normal)
        {
            return;
        }

        services.Notifications.Show(NotificationKind.FirstClose, FirstCloseTitle, FirstCloseText);
        services.Settings.Update(s => s with { Onboarding = s.Onboarding with { CloseNotified = true } }, SettingsChange.Internal);
    }
}
