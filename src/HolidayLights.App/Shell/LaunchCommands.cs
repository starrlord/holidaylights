namespace HolidayLights.App.Shell;

/// <summary>
/// What a later launch asks the running instance to do (PRODUCT-SPEC 6.6.2, 2.5.4): the pipe command for each
/// <see cref="LaunchKind"/> of a normal launch.
/// </summary>
public static class LaunchCommands
{
    /// <summary>The command a launch forwards to the running instance.</summary>
    /// <param name="request">The parsed command line.</param>
    /// <returns>The command, or null when the launch asks nothing of a running instance (<c>--autostart</c>).</returns>
    /// <exception cref="ArgumentException">The launch never reaches the running instance (screen saver, installer, render test, diagnostics).</exception>
    public static InstanceCommand? ForRunningInstance(LaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Kind switch
        {
            LaunchKind.Normal => InstanceCommand.ShowSettings(),
            LaunchKind.Autostart => null,
            LaunchKind.Settings => InstanceCommand.ShowSettings(request.Page),
            LaunchKind.LegacySettings or LaunchKind.ScreenSaverConfigure => InstanceCommand.ShowSettings(SettingsPageId.ScreenSaver),
            LaunchKind.Open => InstanceCommand.Open(request.Files),
            LaunchKind.ToggleLayer => InstanceCommand.Simple(InstanceCommandKind.ToggleLayer),
            LaunchKind.Lights => request.Argument switch
            {
                "on" => InstanceCommand.Lights(true),
                "off" => InstanceCommand.Lights(false),
                _ => InstanceCommand.Simple(InstanceCommandKind.ToggleLights),
            },
            LaunchKind.Theme => InstanceCommand.Theme(request.Argument ?? ""),
            LaunchKind.Exit => InstanceCommand.Simple(InstanceCommandKind.Exit),
            LaunchKind.Reset => InstanceCommand.Simple(InstanceCommandKind.Reset),
            _ => throw new ArgumentException($"A {request.Kind} launch is never forwarded to the running instance.", nameof(request)),
        };
    }

    /// <summary>
    /// True when the forwarded command shows a window of the running instance, so this process (which received the
    /// user's input) lets that instance take the foreground.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>True for <c>show-settings</c>, <c>open</c> and <c>reset</c>.</returns>
    public static bool ShowsWindow(InstanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Kind is InstanceCommandKind.ShowSettings or InstanceCommandKind.Open or InstanceCommandKind.Reset;
    }
}
