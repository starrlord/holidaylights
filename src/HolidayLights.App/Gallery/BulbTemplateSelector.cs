using System.Windows;
using System.Windows.Controls;

namespace HolidayLights.App.Gallery;

/// <summary>Chooses the bulb template or the placeholder template for an item of the Bulb List.</summary>
public sealed class BulbTemplateSelector : DataTemplateSelector
{
    /// <summary>The template of a bulb (a tile or a Details row).</summary>
    public DataTemplate? BulbTemplate { get; set; }

    /// <summary>The template of a placeholder.</summary>
    public DataTemplate? SkeletonTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is SkeletonItem ? SkeletonTemplate : BulbTemplate;
}
