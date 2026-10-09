using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using HolidayLights.App.Shell;

namespace HolidayLights.App.Install;

/// <summary>Installs or uninstalls.</summary>
public enum SetupMode
{
    /// <summary><c>--install</c> (or <c>Setup.exe</c>).</summary>
    Install,

    /// <summary><c>--uninstall</c>.</summary>
    Uninstall,
}

/// <summary>
/// The installer and uninstaller window (PRODUCT-SPEC 6.10): the choice (install, update, reinstall or install an older
/// version, by what is installed; closing a running Holiday Lights 5.4 first; what else the uninstaller removes), the work
/// on a background thread, and the result. UI thread.
/// </summary>
public partial class SetupWindow : Window
{
    private const string LogSource = "Install";

    private readonly Installer installer;
    private readonly SetupMode mode;
    private readonly IAppServices services;
    private readonly InstallKind kind;
    private bool working;
    private bool finished;

    /// <summary>Creates the window.</summary>
    /// <param name="installer">Does the work.</param>
    /// <param name="mode">Install or uninstall.</param>
    /// <param name="services">The services (the 5.4 leftovers, the log).</param>
    public SetupWindow(Installer installer, SetupMode mode, IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(services);
        this.installer = installer;
        this.mode = mode;
        this.services = services;
        InitializeComponent();
        if (mode == SetupMode.Install)
        {
            kind = installer.Kind;
            (Heading.Text, Intro.Text, Primary.Content) = InstallTexts(kind, installer.InstalledVersion, installer.TargetFolder);
            LegacyRunning.Visibility = IsLegacyRunning() ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            Heading.Text = "Uninstall Holiday Lights";
            Primary.Content = "_Uninstall";
            if (installer.IsInstallation)
            {
                Intro.Text = "Uninstalling removes Holiday Lights from your PC. Your bulbs, songs and pictures in Documents\\Holiday Lights are kept unless you choose to remove them.";
                RemoveDocuments.Visibility = Visibility.Visible;
                RemoveSettings.Visibility = Visibility.Visible;
            }
            else
            {
                Intro.Text = "This copy of Holiday Lights wasn't installed with its installer, so there is nothing to uninstall.";
                Primary.IsEnabled = false;
            }
        }

        // Started by the setup, whose own window has just gone away: come to the front (the setup allowed it).
        Loaded += (_, _) => Activate();
    }

    /// <summary>The heading, the text and the button of the install page.</summary>
    /// <param name="kind">What installing does.</param>
    /// <param name="installedVersion">The installed version, or null.</param>
    /// <param name="targetFolder">Where Holiday Lights is installed.</param>
    /// <returns>The texts.</returns>
    internal static (string Heading, string Intro, string Button) InstallTexts(InstallKind kind, string? installedVersion, string targetFolder)
    {
        const string Kept = "Your settings, themes, bulbs, songs and pictures are kept.";
        string version = VersionInfo.ProgramVersion;
        return kind switch
        {
            InstallKind.Update => ("Update Holiday Lights", $"Holiday Lights {installedVersion} is installed. Setup updates it to version {version}. {Kept}", "_Update"),
            InstallKind.Reinstall => ("Reinstall Holiday Lights", $"Holiday Lights {version} is already installed. Installing it again repairs it. {Kept}", "_Reinstall"),
            InstallKind.Downgrade => ("Install an Older Version?",
                $"Holiday Lights {installedVersion} is installed, which is newer than this setup ({version}). Installing replaces it with the older version. {Kept}",
                "_Install Older Version"),
            _ => ("Install Holiday Lights", $"Holiday Lights will be installed for you in {targetFolder}. No administrator rights are needed.", "_Install"),
        };
    }

    /// <summary>The process exit code: 0 when the work was done, 1 when cancelled or failed.</summary>
    public int ExitCode { get; private set; } = 1;

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        // The work cannot be interrupted halfway.
        e.Cancel = working;
        base.OnClosing(e);
    }

    private bool IsLegacyRunning()
    {
        try
        {
            return services.LegacyLeftovers.Detect().IsRunning;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            services.Log.Warn(LogSource, "Holiday Lights 5.4 could not be checked.", e);
            return false;
        }
    }

    private void OnCloseLegacy(object sender, RoutedEventArgs e)
    {
        if (services.LegacyLeftovers.CloseRunningInstance())
        {
            LegacyRunning.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (finished)
        {
            if (mode == SetupMode.Install && ExitCode == 0 && StartProgram.IsChecked == true)
            {
                installer.StartInstalledProgram();
            }

            Close();
            return;
        }

        var choices = new UninstallChoices(RemoveDocuments.IsChecked == true, RemoveSettings.IsChecked == true);
        ShowPage(working: true);
        var progress = new Progress<string>(text => Status.Text = text);
        try
        {
            await Task.Run(() =>
            {
                if (mode == SetupMode.Install)
                {
                    installer.Install(progress);
                }
                else
                {
                    installer.Uninstall(choices, progress);
                }
            }).ConfigureAwait(true);
            ExitCode = 0;
            DoneText.Text = mode == SetupMode.Uninstall ? "Holiday Lights was removed."
                : kind == InstallKind.Update ? $"Holiday Lights is updated to version {VersionInfo.ProgramVersion}."
                : "Holiday Lights is installed.";
            StartProgram.Visibility = mode == SetupMode.Install ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            services.Log.Error(LogSource, $"{mode} failed.", exception);
            DoneText.Text = mode == SetupMode.Install
                ? $"Holiday Lights could not be installed: {exception.Message}"
                : $"Holiday Lights could not be removed completely: {exception.Message}";
        }

        ShowPage(working: false);
    }

    private void ShowPage(bool working)
    {
        this.working = working;
        finished = !working;
        ChoicePage.Visibility = Visibility.Collapsed;
        WorkingPage.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        DonePage.Visibility = working ? Visibility.Collapsed : Visibility.Visible;
        Cancel.Visibility = Visibility.Collapsed;
        Primary.IsEnabled = !working;
        Primary.Content = mode == SetupMode.Install && ExitCode == 0 ? "_Finish" : "_Close";
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!working)
        {
            Close();
        }
    }
}
