using System.Windows;

namespace HolidayLights.App.Preview;

/// <summary>
/// Attached, inherited preview state (PRODUCT-SPEC 3.0.3, 4.4): containers (tiles, theme cards) set <c>IsHot</c> while
/// hovered or focused. Bulb previews inside them animate only then when Windows animation effects are off, and theme
/// cards animate only then at all.
/// </summary>
public static class PreviewAnimation
{
    /// <summary>Identifies the attached, inherited <c>IsHot</c> property.</summary>
    public static readonly DependencyProperty IsHotProperty =
        DependencyProperty.RegisterAttached("IsHot", typeof(bool), typeof(PreviewAnimation),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>True while the container is hovered or focused.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The value.</returns>
    public static bool GetIsHot(DependencyObject element) => (bool)element.GetValue(IsHotProperty);

    /// <summary>Sets <c>IsHot</c>.</summary>
    /// <param name="element">The element.</param>
    /// <param name="value">The value.</param>
    public static void SetIsHot(DependencyObject element, bool value) => element.SetValue(IsHotProperty, value);

    /// <summary>True when bulb previews step with the pattern: always, or only while hot when animation effects are off.</summary>
    /// <param name="element">A preview element.</param>
    /// <returns>True when it should animate.</returns>
    public static bool ShouldStep(DependencyObject element) => SystemParameters.ClientAreaAnimation || GetIsHot(element);
}
