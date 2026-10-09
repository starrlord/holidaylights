using System.Diagnostics;
using System.IO;
using HolidayLights.App.Install;
using HolidayLights.App.ScreenSaver;
using HolidayLights.App.Shell;
using HolidayLights.Platform.Instance;

namespace HolidayLights.App;

/// <summary>
/// The custom entry point (owner: app-shell; ARCHITECTURE 7, PRODUCT-SPEC 6.2.1, 6.6.3):
/// <list type="number">
/// <item>parse the command line (<c>--data-root &lt;dir&gt;</c> and <c>--no-system-changes</c> first; legacy forms too);</item>
/// <item>screen saver arguments (<c>/s</c>, <c>/p &lt;hwnd&gt;</c>, <c>/p:&lt;hwnd&gt;</c>, <c>/c[:hwnd]</c>, <c>/a</c>) before anything
/// else; saver processes never take the single-instance mutex;</item>
/// <item><c>--render-test</c>, <c>--install</c>, <c>--uninstall</c> run without the mutex;</item>
/// <item>single instance: a later launch forwards its command over the pipe and exits;</item>
/// <item>then WPF: <see cref="App"/>, the composition root, lights first, the tray, the first-run flow.</item>
/// </list>
/// </summary>
public static class Program
{
    private const string LogSource = "Program";

    /// <summary>The process entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code (0 on success).</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        long started = Stopwatch.GetTimestamp();
        LaunchRequest request = CommandLine.Parse(args);
        DataPaths paths = ResolvePaths(request);
        AppRuntimeOptions options = AppRuntimeOptions.FromEnvironment();
        if (request.NoSystemChanges)
        {
            options = options with { AllowSystemChanges = false };
        }

        using var log = new RollingFileLog(paths);
        if (request.UnknownArguments.Count > 0)
        {
            log.Warn(LogSource, $"{request.UnknownArguments.Count} command-line argument(s) were not understood and are ignored.");
        }

        try
        {
            return request.Kind switch
            {
                LaunchKind.ScreenSaverPassword => 0,
                LaunchKind.ScreenSaverShow => RunScreenSaver(paths, options with { Session = AppSessionKind.ScreenSaver }, log, request, ScreenSaverEntry.RunFullScreen),
                LaunchKind.ScreenSaverPreview => request.WindowHandle == 0
                    ? 1
                    : RunScreenSaver(paths, options with { Session = AppSessionKind.ScreenSaverPreview }, log, request, host => ScreenSaverEntry.RunPreview(host, request.WindowHandle)),
                LaunchKind.ScreenSaverConfigure or LaunchKind.LegacySettings => RunSettingsOnly(paths, options, log, request, started),
                LaunchKind.RenderTest => RunWithoutWindows(paths, options with { Session = AppSessionKind.RenderTest }, log, host => RenderTest.Run(host, request.Argument!)),
                LaunchKind.Diagnostics => RunWithoutWindows(paths, options with { Session = AppSessionKind.RenderTest }, log, host => DiagnosticsReport.Run(host, request.Argument)),
                LaunchKind.Install => RunSetup(paths, options, log, request.Quiet ? PerUserSetup.InstallQuietly : PerUserSetup.Install),
                LaunchKind.Uninstall => RunSetup(paths, options, log, PerUserSetup.Uninstall),
                _ => RunNormal(paths, options, log, request, started),
            };
        }
        catch (Exception e)
        {
            log.Error(LogSource, "Holiday Lights could not run.", e);
            return 1;
        }
    }

    /// <summary>The normal app, or a later launch that forwards its command to the running instance.</summary>
    private static int RunNormal(DataPaths paths, AppRuntimeOptions options, RollingFileLog log, LaunchRequest request, long started)
    {
        // A data root has its own mutex and pipe, so an isolated launch never forwards to another session.
        using var instance = new SingleInstance(log, paths.DataRoot);
        if (!instance.TryClaim())
        {
            InstanceCommand? command = LaunchCommands.ForRunningInstance(request);
            return command is null || InstanceClient.ForwardAsync(instance, command, log).GetAwaiter().GetResult() ? 0 : 1;
        }

        if (request.Kind == LaunchKind.Exit)
        {
            // Nothing is running, so there is nothing to end.
            return 0;
        }

        options = options with { Session = AppSessionKind.Normal, StartedAtSignIn = request.Kind == LaunchKind.Autostart };
        return RunWpf(paths, options, log, instance, request, started);
    }

    /// <summary>
    /// Windows' screen saver "Settings" button and the legacy <c>settings</c>: Settings on Screen Saver in the running
    /// instance, else a settings-only session (no lights, tray, hot keys or music) that ends with the window.
    /// </summary>
    private static int RunSettingsOnly(DataPaths paths, AppRuntimeOptions options, RollingFileLog log, LaunchRequest request, long started)
    {
        if (RunningInstance.IsPresent(paths.DataRoot))
        {
            using var instance = new SingleInstance(log, paths.DataRoot);
            if (InstanceClient.ForwardAsync(instance, InstanceCommand.ShowSettings(SettingsPageId.ScreenSaver), log).GetAwaiter().GetResult())
            {
                return 0;
            }
        }

        return RunWpf(paths, options with { Session = AppSessionKind.SettingsOnly }, log, null, request, started);
    }

    /// <summary>Runs a WPF session (normal or settings-only) until it shuts down.</summary>
    private static int RunWpf(DataPaths paths, AppRuntimeOptions options, RollingFileLog log, ISingleInstance? instance, LaunchRequest request, long started)
    {
        AppHost? host = null;
        var app = new App(log, () => host?.FlushForSessionEnd());
        host = new AppHost(paths, options, log, instance) { ProcessStartTimestamp = started };
        AppServicesHost.Initialize(host);
        app.Startup += async (_, _) =>
        {
            try
            {
                await host.StartAsync(request).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                log.Error(LogSource, "Holiday Lights could not start.", e);
                await host.DisposeAsync().ConfigureAwait(true);
                app.Shutdown(1);
            }
        };

        int exitCode = app.Run();
        host.ShutDownNow();
        return exitCode;
    }

    /// <summary>The screen saver (<c>/s</c>) or its preview (<c>/p</c>): never takes the mutex; the entry runs its own message loop.</summary>
    private static int RunScreenSaver(DataPaths paths, AppRuntimeOptions options, RollingFileLog log, LaunchRequest request, Func<IAppServices, int> run)
    {
        _ = new App(log, () => { });
        var host = new AppHost(paths, options, log, instance: null);
        AppServicesHost.Initialize(host);
        host.StartAsync(request).GetAwaiter().GetResult();
        try
        {
            return run(host);
        }
        finally
        {
            host.ShutDownNow();
        }
    }

    /// <summary><c>--render-test</c> and <c>--diagnostics</c>: no windows, no mutex.</summary>
    private static int RunWithoutWindows(DataPaths paths, AppRuntimeOptions options, RollingFileLog log, Func<AppHost, int> run)
    {
        var host = new AppHost(paths, options, log, instance: null);
        try
        {
            return run(host);
        }
        finally
        {
            host.ShutDownNow();
        }
    }

    /// <summary>The per-user installer or uninstaller (with its own small window).</summary>
    private static int RunSetup(DataPaths paths, AppRuntimeOptions options, RollingFileLog log, Func<IAppServices, int> run)
    {
        // Whoever started it (Windows Settings > Apps, the Start menu, General), the current folder must not be the program
        // folder, which the uninstaller removes and an installer may replace.
        TryLeaveCurrentFolder(log);
        _ = new App(log, () => { });
        var host = new AppHost(paths, options with { Session = AppSessionKind.Setup }, log, instance: null);
        AppServicesHost.Initialize(host);
        try
        {
            return run(host);
        }
        finally
        {
            host.ShutDownNow();
        }
    }

    /// <summary>Makes the temporary folder the current folder (a folder that is some process's current folder cannot be removed).</summary>
    private static void TryLeaveCurrentFolder(RollingFileLog log)
    {
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log.Warn(LogSource, "The current folder could not be changed.", e);
        }
    }

    /// <summary>The data paths: <c>--data-root</c> moves every user location (and is passed on to child processes), else the environment decides.</summary>
    private static DataPaths ResolvePaths(LaunchRequest request)
    {
        if (request.DataRoot is not { } root)
        {
            return DataPaths.FromEnvironment();
        }

        Environment.SetEnvironmentVariable(DataPaths.DataRootVariable, root);
        return DataPaths.ForDataRoot(root);
    }
}
