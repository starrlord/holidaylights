using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HolidayLights.App.BulbFactory;

/// <summary>Base of the Bulb Factory view models: property change notification.</summary>
internal abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets a field and raises <see cref="PropertyChanged"/> when the value changed.</summary>
    /// <typeparam name="T">The field type.</typeparam>
    /// <param name="field">The field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property (filled in by the compiler).</param>
    /// <returns>True when the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    /// <param name="propertyName">The property, or null for all.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
