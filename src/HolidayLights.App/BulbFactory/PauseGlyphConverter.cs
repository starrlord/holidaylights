using System.Globalization;
using System.Windows.Data;

namespace HolidayLights.App.BulbFactory;

/// <summary>The glyph of the preview's pause button: Pause (U+E769) while playing, Play (U+E768) while paused (Segoe Fluent Icons).</summary>
internal sealed class PauseGlyphConverter : IValueConverter
{
    private const string PauseGlyph = "";
    private const string PlayGlyph = "";

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? PlayGlyph : PauseGlyph;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
