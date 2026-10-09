using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HolidayLights.App.Preview;

/// <summary>
/// When bulb previews may draw new frames (PRODUCT-SPEC 3.0.2, 3.0.3, 5.12.2): never while nobody can see them - the
/// session is locked, the display is off or the user is away (the desktop lights rest then too) - nor in a minimized
/// window; and items scrolled out of their list's viewport wait until they scroll back in.
/// </summary>
public static class PreviewActivity
{
    /// <summary>The pause reasons that stop the previews as well: nobody can see the screen.</summary>
    public const PauseReasons RestingReasons = PauseReasons.SessionLocked | PauseReasons.DisplayOff | PauseReasons.UserNotPresent;

    /// <summary>True while the lights rest because nobody can see the screen (previews draw nothing new then).</summary>
    /// <param name="services">The services.</param>
    /// <returns>True while resting.</returns>
    public static bool IsResting(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return (services.Lights.Status.Paused & RestingReasons) != PauseReasons.None;
    }

    /// <summary>True when the window is minimized.</summary>
    /// <param name="window">The window, or null.</param>
    /// <returns>True when minimized.</returns>
    public static bool IsMinimized(Window? window) => window is { WindowState: WindowState.Minimized };

    /// <summary>
    /// True when any part of an element lies inside the viewport of the nearest scroll viewer around it (elements outside
    /// any scroll viewer count as shown). Realized items in a list's cache area are not shown.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>True when shown.</returns>
    public static bool IsInViewport(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        DependencyObject? node = VisualTreeHelper.GetParent(element);
        while (node is not null and not ScrollViewer)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        if (node is not ScrollViewer viewer || viewer.ActualWidth <= 0 || viewer.ActualHeight <= 0)
        {
            return true;
        }

        try
        {
            Rect bounds = element.TransformToAncestor(viewer).TransformBounds(new Rect(element.RenderSize));
            return bounds.IntersectsWith(new Rect(viewer.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
