using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace HolidayLights.App.Controls;

/// <summary>
/// A panel that screen readers see as a named group (the edge and corner boxes of the frame editor: "Top edge, 2 of 6
/// bulb types: Standard Bulbs, Snow Family."). Its name and help text come from the <c>AutomationProperties</c>.
/// </summary>
public class AutomationGroup : Grid
{
    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupAutomationPeer(this);

    private sealed class GroupAutomationPeer(AutomationGroup owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(AutomationGroup);

        protected override bool IsControlElementCore() => true;
    }
}
