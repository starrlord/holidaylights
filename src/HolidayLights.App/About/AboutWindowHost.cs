using System.Windows;

namespace HolidayLights.App.About;

/// <summary>
/// About Holiday Lights (PRODUCT-SPEC 3.9, 6.5): 640 x 720, modeless, single instance; the 5.4 ABOUT banner with the
/// alternating ABOUTFLASH strip, "Modern Edition 6.0", credits expanders (from <c>HelpContentStore.LoadCredits</c>),
/// "Copy Version Info". Owner: app-shell.
/// </summary>
public static class AboutWindowHost
{
    private static AboutWindow? open;

    /// <summary>Opens the window or brings it forward.</summary>
    /// <param name="services">The services.</param>
    public static void Show(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (open is null)
        {
            open = new AboutWindow(services, HelpContentStore.LoadCredits());
            open.Closed += (_, _) => open = null;
            open.Show();
        }

        if (open.WindowState == WindowState.Minimized)
        {
            open.WindowState = WindowState.Normal;
        }

        open.Activate();
    }
}
