using System.Windows.Input;

namespace HolidayLights.App.Shell;

/// <summary>A command that runs an action (InfoBar actions of the shell's windows).</summary>
/// <param name="execute">The action.</param>
internal sealed class RelayCommand(Action execute) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc />
    public void Execute(object? parameter) => execute();
}
