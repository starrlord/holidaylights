using System.IO;
using System.Security;
using System.Windows;

namespace HolidayLights.App.Install;

/// <summary>
/// The per-user installer and uninstaller (PRODUCT-SPEC 6.10; owner: app-shell): no administrator rights; installs into
/// <c>%LOCALAPPDATA%\Programs\HolidayLights\</c> (<c>HolidayLights.exe</c>, <c>Holiday Lights.scr</c>, <c>Content\</c>),
/// one Start menu entry, the <c>.bul</c> association, <c>HKCU\...\Uninstall\HolidayLights</c>; offers to close a running
/// 5.4; the uninstaller restores the previous screen saver, removes the Run value and association, and asks before
/// removing user files. Honours <see cref="AppRuntimeOptions.AllowSystemChanges"/> and the data root.
/// </summary>
public static class PerUserSetup
{
    /// <summary><c>--install</c>: installs from the folder this program runs from.</summary>
    /// <param name="services">The services (paths, options, platform registrations, log).</param>
    /// <returns>The process exit code.</returns>
    public static int Install(IAppServices services) => Run(services, SetupMode.Install);

    /// <summary>
    /// <c>--install --quiet</c> (the setup's <c>/SILENT</c> and <c>/VERYSILENT</c>): installs without a window and starts
    /// Holiday Lights again when it was running before.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>0 when installed, 1 when it failed (the log says why).</returns>
    public static int InstallQuietly(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var installer = new Installer(services);
        try
        {
            installer.Install();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            services.Log.Error("Install", "Install failed.", e);
            return 1;
        }

        if (installer.ClosedRunningProgram)
        {
            installer.StartInstalledProgram();
        }

        return 0;
    }

    /// <summary><c>--uninstall</c>: uninstalls this installation.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The process exit code.</returns>
    public static int Uninstall(IAppServices services) => Run(services, SetupMode.Uninstall);

    /// <summary>Shows the setup window and runs the message loop until it closes.</summary>
    private static int Run(IAppServices services, SetupMode mode)
    {
        ArgumentNullException.ThrowIfNull(services);
        Application application = Application.Current
            ?? throw new InvalidOperationException("The setup window needs the application object.");
        var window = new SetupWindow(new Installer(services), mode, services);
        window.Closed += (_, _) => application.Shutdown(window.ExitCode);
        return application.Run(window);
    }
}
