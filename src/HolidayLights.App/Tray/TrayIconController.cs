using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using HolidayLights.App.Shell;
using HolidayLights.Audio;

namespace HolidayLights.App.Tray;

/// <summary>
/// The tray icon and menu (PRODUCT-SPEC 2.2): H.NotifyIcon <c>TaskbarIcon</c> created with
/// <c>ForceCreate(enablesEfficiencyMode: false)</c>, <c>MenuActivation = LeftOrRightClick</c>, <c>NoLeftClickDelay</c>,
/// double-click and Enter open Settings; lit/unlit icons for light/dark taskbars; the 3-line tooltip; the Fluent
/// <c>ContextMenu</c> with the live header strip and the Themes submenu. Owner: app-shell.
/// </summary>
/// <remarks>
/// One left or right click opens the menu at once, on button release (5.4's "click once, not twice"); the second click of
/// a double-click arrives as <c>WM_LBUTTONDBLCLK</c>, which closes the menu and opens Settings on its last page. From the
/// keyboard (Win+B, arrows), Enter or Space opens Settings and Shift+F10 or the Menu key opens the menu. H.NotifyIcon
/// re-adds the icon when Explorer restarts (<c>TaskbarCreated</c>). The menu is rebuilt each time it opens. UI thread.
/// </remarks>
public sealed class TrayIconController : IDisposable
{
    private const string LogSource = "Tray";

    private readonly IAppServices services;
    private readonly Func<LightsPauseState> pause;
    private readonly Action balloonClicked;
    private readonly TimeProvider time;
    private readonly Dispatcher dispatcher;
    private readonly TrayIcons icons = new();
    private readonly ContextMenu menu = new();
    private TaskbarIcon? icon;
    private (bool Lit, bool LightTaskbar)? shownLook;
    private bool disposed;

    /// <summary>Creates the icon (not shown yet).</summary>
    /// <param name="services">The services.</param>
    public TrayIconController(IAppServices services)
        : this(services, () => LightsPauseState.None, () => { }, TimeProvider.System)
    {
    }

    /// <summary>Creates the icon with the composition root's pause state and notification clicks.</summary>
    /// <param name="services">The services.</param>
    /// <param name="pause">What rests now (the tooltip says "resting while a full-screen app is open").</param>
    /// <param name="balloonClicked">Called when the user clicks a notification.</param>
    /// <param name="time">The clock (today's Automatic theme).</param>
    internal TrayIconController(IAppServices services, Func<LightsPauseState> pause, Action balloonClicked, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(balloonClicked);
        ArgumentNullException.ThrowIfNull(time);
        this.services = services;
        this.pause = pause;
        this.balloonClicked = balloonClicked;
        this.time = time;
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Shows the icon in the notification area.</summary>
    public void Show()
    {
        if (icon is not null || disposed)
        {
            return;
        }

        icon = new TaskbarIcon
        {
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.LeftOrRightClick,
            NoLeftClickDelay = true,
            ToolTipText = "Holiday Lights",
        };
        icon.PreviewTrayContextMenuOpen += OnMenuOpening;
        icon.TrayMouseDoubleClick += OnDoubleClick;
        icon.TrayKeyboardKeySelect += OnKeySelect;
        icon.TrayKeyboardContextMenu += OnKeyboardMenu;
        icon.TrayBalloonTipClicked += OnBalloonClicked;
        Refresh();
        icon.ForceCreate(enablesEfficiencyMode: false);

        services.Settings.Changed += OnSettingsChanged;
        services.Lights.StatusChanged += OnStateChanged;
        services.Lights.SceneChanged += OnStateChanged;
        services.SystemInfo.Changed += OnStateChanged;
        services.Themes.Changed += OnStateChanged;
        services.Music.StateChanged += OnMusicStateChanged;
        services.Log.Info(LogSource, "Tray icon shown.");
    }

    /// <summary>Updates the icon and the tooltip (lights on or off, theme, location, resting, song).</summary>
    /// <remarks>
    /// Runs inside the settings, scene and status events, so it never throws: a failure is logged and the other
    /// subscribers of those events still see the change.
    /// </remarks>
    public void Refresh()
    {
        if (icon is null || disposed)
        {
            return;
        }

        try
        {
            AppSettings settings = services.Settings.Current;
            (bool Lit, bool LightTaskbar) look = (settings.Lights.On, services.SystemInfo.TaskbarUsesLightTheme);
            if (shownLook != look)
            {
                // TaskbarIcon disposes the icon it replaces, so every change hands it a new copy.
                shownLook = null;
                icon.Icon = icons.Create(look.Lit, look.LightTaskbar);
                shownLook = look;
            }

            MusicState music = services.Music.State;
            string? song = music.Status == MusicStatus.Playing ? music.CurrentSong?.Title : null;
            TrayMenuState state = MenuState(settings);
            string theme = TrayMenuModel.IsAutomatic(state) ? $"Automatic: {state.MatchingTheme}" : state.MatchingTheme ?? TrayMenuModel.CustomSettings;
            icon.ToolTipText = TrayTooltip.Build(new TrayTooltipState(settings.Lights.On, theme, services.Lights.Scene, services.Lights.Status, pause(), song)
            {
                MusicWaiting = music.Status == MusicStatus.WaitingForSynthesizer,
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The tray icon could not be updated.", e);
        }
    }

    /// <summary>Shows a balloon notification (Windows 11 shows it as a toast), without sound and respecting quiet time.</summary>
    /// <param name="title">The title.</param>
    /// <param name="text">The text.</param>
    /// <returns>True when it was handed to Windows.</returns>
    public bool ShowNotification(string title, string text)
    {
        if (icon is null || disposed)
        {
            return false;
        }

        try
        {
            icon.ShowNotification(title, text, NotificationIcon.None, customIconHandle: null, largeIcon: false, sound: false, respectQuietTime: true, realtime: false, timeout: null);
            return true;
        }
        catch (InvalidOperationException e)
        {
            services.Log.Warn(LogSource, "A notification could not be shown.", e);
            return false;
        }
    }

    /// <summary>Removes the icon.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (icon is not null)
        {
            services.Settings.Changed -= OnSettingsChanged;
            services.Lights.StatusChanged -= OnStateChanged;
            services.Lights.SceneChanged -= OnStateChanged;
            services.SystemInfo.Changed -= OnStateChanged;
            services.Themes.Changed -= OnStateChanged;
            services.Music.StateChanged -= OnMusicStateChanged;
            menu.IsOpen = false;
            icon.Dispose();
            icon = null;
        }

        icons.Dispose();
    }

    private TrayMenuState MenuState(AppSettings settings)
    {
        DateOnly today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        MusicStatus music = services.Music.State.Status;
        IReadOnlyList<HotKeyStatus> hotKeys = services.HotKeys.Status;
        return new TrayMenuState(
            settings,
            services.ThemeService.FindMatching(settings)?.Name,
            services.Calendar.Resolve(settings.Calendar, today).ThemeName,
            [.. services.Themes.Themes.Select(t => t.Name)],
            music is MusicStatus.Playing or MusicStatus.BetweenSongs or MusicStatus.Paused,
            Gesture(hotKeys, HotKeyAction.ToggleLights),
            Gesture(hotKeys, HotKeyAction.SwitchLocation));
    }

    private static string? Gesture(IReadOnlyList<HotKeyStatus> statuses, HotKeyAction action) =>
        statuses.FirstOrDefault(s => s.Action == action) is { Registration: HotKeyRegistration.Registered } status
            ? status.Binding.Format("+")
            : null;

    private void OnMenuOpening(object sender, RoutedEventArgs e)
    {
        TrayMenuState state = MenuState(services.Settings.Current);
        TrayMenuView.Fill(menu, TrayMenuModel.Build(state), TrayMenuModel.Header(state), services, services.SystemInfo.AnimationsEnabled, Execute);
    }

    private void OnDoubleClick(object sender, RoutedEventArgs e)
    {
        menu.IsOpen = false;
        services.SettingsWindow.Show();
    }

    private void OnKeySelect(object sender, RoutedEventArgs e) => services.SettingsWindow.Show();

    private void OnKeyboardMenu(object sender, RoutedEventArgs e)
    {
        // A right-click also ends with WM_CONTEXTMENU; its menu is already open then.
        if (!menu.IsOpen && icon is not null)
        {
            icon.ShowContextMenu(TaskbarIcon.GetPopupTrayPosition());
        }
    }

    private void OnBalloonClicked(object sender, RoutedEventArgs e) => balloonClicked();

    private void Execute(TrayCommand command, string? themeName)
    {
        ISettingsStore store = services.Settings;
        AppSettings current = store.Current;
        switch (command)
        {
            case TrayCommand.ShowLights:
                SettingsActions.SetLightsOn(!current.Lights.On).ApplyTo(store);
                break;
            case TrayCommand.OnDesktop:
                SettingsActions.SetDrawing(BulbDrawing.Desktop).ApplyTo(store);
                break;
            case TrayCommand.OnTop:
                SettingsActions.SetDrawing(BulbDrawing.OnTop).ApplyTo(store);
                break;
            case TrayCommand.AutomaticThemes:
                SettingsActions.SetAutomaticThemes(!current.Calendar.Enabled).ApplyTo(store);
                break;
            case TrayCommand.LoadTheme when services.Themes.Find(themeName ?? "") is { } theme:
                SettingsActions.LoadTheme(services.ThemeService, theme, time.GetLocalNow()).ApplyTo(store);
                break;
            case TrayCommand.ManageThemes:
                services.SettingsWindow.Show(SettingsPageId.Themes);
                break;
            case TrayCommand.PlayMusic:
                SettingsActions.SetMusicEnabled(!current.Music.Enabled).ApplyTo(store);
                break;
            case TrayCommand.NextSong:
                services.Music.NextSong();
                break;
            case TrayCommand.Settings:
                services.SettingsWindow.Show();
                break;
            case TrayCommand.Help:
                services.AppShell.ShowHelp();
                break;
            case TrayCommand.About:
                services.AppShell.ShowAbout();
                break;
            case TrayCommand.Exit:
                _ = services.AppShell.ExitAsync();
                break;
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Refresh();

    private void OnStateChanged(object? sender, EventArgs e) => Refresh();

    private void OnMusicStateChanged(object? sender, EventArgs e)
    {
        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(Refresh);
        }
    }
}
