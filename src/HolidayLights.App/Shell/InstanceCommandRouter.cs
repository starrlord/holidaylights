namespace HolidayLights.App.Shell;

/// <summary>The result of a command from another launch.</summary>
/// <param name="Accepted">The reply on the pipe.</param>
/// <param name="AfterReply">What to do once the reply was sent (exit), or null.</param>
public sealed record CommandOutcome(bool Accepted, Func<Task>? AfterReply = null)
{
    /// <summary>Accepted, nothing more to do.</summary>
    public static CommandOutcome Done { get; } = new(true);

    /// <summary>Refused (unknown theme, a command without its argument).</summary>
    public static CommandOutcome Refused { get; } = new(false);
}

/// <summary>
/// Performs the commands of later launches and of the command line of the first launch (CONTRACTS 6.4; PRODUCT-SPEC
/// 6.6.2, 6.6.3, 2.3 "Which page opens"). UI thread.
/// </summary>
public sealed class InstanceCommandRouter
{
    private readonly IAppServices services;
    private readonly HotKeyController hotKeys;
    private readonly TimeProvider time;

    /// <summary>Creates the router.</summary>
    /// <param name="services">The services.</param>
    /// <param name="hotKeys">Performs the location and lights toggles with their pill.</param>
    /// <param name="time">The clock (Recent Settings dates).</param>
    public InstanceCommandRouter(IAppServices services, HotKeyController hotKeys, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hotKeys);
        ArgumentNullException.ThrowIfNull(time);
        this.services = services;
        this.hotKeys = hotKeys;
        this.time = time;
    }

    /// <summary>Performs a command.</summary>
    /// <param name="command">The command (screen saver and music-stream commands are the pipe server's).</param>
    /// <returns>Whether it was accepted, and what follows the reply.</returns>
    public CommandOutcome Execute(InstanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlyList<string> arguments = command.Arguments;
        switch (command.Kind)
        {
            case InstanceCommandKind.ShowSettings:
                services.SettingsWindow.Show(arguments.Count > 0 && InstanceCommand.TryParsePageName(arguments[0], out SettingsPageId page) ? page : null);
                return CommandOutcome.Done;
            case InstanceCommandKind.Open when arguments.Count > 0:
                services.SettingsWindow.Show(SettingsPageId.BulbFactory, new OpenFilesRequest(arguments));
                return CommandOutcome.Done;
            case InstanceCommandKind.ToggleLights:
                hotKeys.SetLights(!services.Settings.Current.Lights.On);
                return CommandOutcome.Done;
            case InstanceCommandKind.Lights when arguments.Count > 0 && arguments[0] is "on" or "off":
                hotKeys.SetLights(arguments[0] == "on");
                return CommandOutcome.Done;
            case InstanceCommandKind.ToggleLayer:
                hotKeys.SwitchLocation();
                return CommandOutcome.Done;
            case InstanceCommandKind.Theme when arguments.Count > 0:
                return LoadTheme(arguments[0]);
            case InstanceCommandKind.Exit:
                return new CommandOutcome(true, services.AppShell.ExitAsync);
            case InstanceCommandKind.Reset:
                services.SettingsWindow.Show(SettingsPageId.General, new ResetRequest());
                return CommandOutcome.Done;
            default:
                services.Log.Warn("Shell.Instance", $"Refused the command {command}.");
                return CommandOutcome.Refused;
        }
    }

    private CommandOutcome LoadTheme(string name)
    {
        ThemeDefinition? theme = services.Themes.Find(name.Trim());
        if (theme is null)
        {
            services.Log.Warn("Shell.Instance", "A theme to load was not found.");
            return CommandOutcome.Refused;
        }

        SettingsActions.LoadTheme(services.ThemeService, theme, time.GetLocalNow()).ApplyTo(services.Settings);
        return CommandOutcome.Done;
    }
}
