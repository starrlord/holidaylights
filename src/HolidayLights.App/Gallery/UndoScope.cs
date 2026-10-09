using System.Windows;
using HolidayLights.App.Settings;

namespace HolidayLights.App.Gallery;

/// <summary>Finds the undo history that a command of the Bulb List or the selected-bulb bar records in.</summary>
internal static class UndoScope
{
    /// <summary>
    /// The history of the Settings window that shows <paramref name="element"/>; elsewhere (the Choose a Bulb dialog,
    /// which belongs to the open Settings window) the application's current history.
    /// </summary>
    /// <param name="element">An element of the list or the bar.</param>
    /// <param name="services">The application services.</param>
    /// <returns>The history.</returns>
    public static IUndoHistory Of(DependencyObject element, IAppServices services) =>
        Window.GetWindow(element) is ISettingsHost host ? host.History : services.Undo;
}
