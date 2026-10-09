using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HolidayLights.App.BulbFactory;

/// <summary>Shows an element only while a bound text (a message) is set: null or empty is <see cref="Visibility.Collapsed"/>.</summary>
internal sealed class TextVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
