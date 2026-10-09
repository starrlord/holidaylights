using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace HolidayLights.App.Controls;

/// <summary>Walks up from any element (visuals and content elements such as a <c>Run</c>) to its containers.</summary>
public static class ElementTree
{
    /// <summary>The visual parent of a visual, else the logical parent (content elements have no visual parent).</summary>
    /// <param name="element">The element.</param>
    /// <returns>The parent, or null at the root.</returns>
    public static DependencyObject? Parent(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
    }

    /// <summary>The element itself and its containers, nearest first.</summary>
    /// <param name="element">The element, or null.</param>
    /// <returns>The chain up to the root.</returns>
    public static IEnumerable<DependencyObject> SelfAndAncestors(DependencyObject? element)
    {
        for (DependencyObject? node = element; node is not null; node = Parent(node))
        {
            yield return node;
        }
    }

    /// <summary>True when an element is a container itself or lies inside it.</summary>
    /// <param name="element">The element, or null.</param>
    /// <param name="container">The container.</param>
    /// <returns>True when inside.</returns>
    public static bool IsInside(DependencyObject? element, DependencyObject container) =>
        SelfAndAncestors(element).Any(node => ReferenceEquals(node, container));
}
