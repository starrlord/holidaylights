using System.Windows;

namespace HolidayLights.App.Help;

/// <summary>
/// Holiday Lights Help (PRODUCT-SPEC 3.10): 960 x 720, resizable, modeless, single instance; search and Contents tree
/// from <c>HelpContentStore</c>; topics rendered from Markdown with the 5.4 help banner on top; Back/Forward; "Open the
/// &lt;page&gt; page" buttons. Owner: app-shell.
/// </summary>
public static class HelpWindowHost
{
    private static HelpWindow? open;

    /// <summary>Opens the window on a topic, or brings it forward and navigates.</summary>
    /// <param name="services">The services.</param>
    /// <param name="topicId">A <see cref="HelpTopics"/> id, or null for Contents.</param>
    public static void Show(IAppServices services, string? topicId)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (open is null)
        {
            open = new HelpWindow(services, HelpContentStore.LoadContents());
            open.Closed += (_, _) => open = null;
            open.Navigate(topicId ?? HelpTopics.Welcome);
            open.Show();
        }
        else if (topicId is not null)
        {
            open.Navigate(topicId);
        }

        if (open.WindowState == WindowState.Minimized)
        {
            open.WindowState = WindowState.Normal;
        }

        open.Activate();
        if (topicId is null)
        {
            // "Opens Holiday Lights Help on its Contents": the Contents tree has the focus.
            open.ContentsTree.Focus();
        }
    }
}
