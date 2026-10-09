using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HolidayLights.App.Controls;

/// <summary>
/// The night well (owner: settings-ui; PRODUCT-SPEC 4.2): the dark gradient behind every bulb picture in the UI, in light
/// and dark mode (High Contrast: the system Window colour), with the 4 DIP chip and tile radius. Default style in
/// <c>Themes/Generic.xaml</c> (background <see cref="AppResourceKeys.NightWellBrush"/>).
/// </summary>
/// <remarks>The child is clipped to the rounded corners, so bulb art never pokes out of the well.</remarks>
public class NightWell : Border
{
    static NightWell() => DefaultStyleKeyProperty.OverrideMetadata(typeof(NightWell), new FrameworkPropertyMetadata(typeof(NightWell)));

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);
        double radius = CornerRadius.TopLeft;
        Clip = arranged.Width > 0 && arranged.Height > 0
            ? new RectangleGeometry(new Rect(arranged), radius, radius)
            : null;
        return arranged;
    }
}
