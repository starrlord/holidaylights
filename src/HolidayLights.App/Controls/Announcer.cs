using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace HolidayLights.App.Controls;

/// <summary>
/// UI Automation announcements (PRODUCT-SPEC 3.0.4): every arrangement, theme and song change raises a polite
/// notification that states the result ("Candy Canes now on the whole frame."), and InfoBars, snackbars and result lines
/// are polite live regions.
/// </summary>
public static class Announcer
{
    /// <summary>The activity id of result announcements; a newer one replaces an older one that was not read yet.</summary>
    public const string ResultActivity = "HolidayLights.Result";

    /// <summary>Raises a polite UIA notification on behalf of an element (Narrator and NVDA read it).</summary>
    /// <param name="source">The element the result belongs to (any element of the window).</param>
    /// <param name="text">The sentence, e.g. "Candy Canes now on the whole frame.".</param>
    public static void Announce(UIElement source, string text)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(text) || !AutomationPeer.ListenerExists(AutomationEvents.Notification))
        {
            return;
        }

        AutomationPeer? peer = UIElementAutomationPeer.FromElement(source) ?? UIElementAutomationPeer.CreatePeerForElement(source);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.MostRecent,
            text,
            ResultActivity);
    }

    /// <summary>Raised before a live region is announced (tests watch it; screen readers need not be running).</summary>
    internal static event Action<UIElement>? LiveRegionRaising;

    /// <summary>Tells screen readers that a polite live region (InfoBar, snackbar, result line) changed its text.</summary>
    /// <param name="region">The element with <see cref="AutomationProperties.LiveSettingProperty"/> set.</param>
    public static void RaiseLiveRegionChanged(UIElement region)
    {
        ArgumentNullException.ThrowIfNull(region);
        LiveRegionRaising?.Invoke(region);
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            return;
        }

        AutomationPeer? peer = UIElementAutomationPeer.FromElement(region) ?? UIElementAutomationPeer.CreatePeerForElement(region);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
