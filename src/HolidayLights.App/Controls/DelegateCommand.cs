using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// An <see cref="ICommand"/> that runs a delegate and can always run (InfoBar actions, key bindings, the Undo split
/// button). Controls that can be unavailable set <c>IsEnabled</c> themselves.
/// </summary>
public sealed class DelegateCommand : ICommand
{
    private readonly Action execute;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">What to run; the command parameter is ignored.</param>
    public DelegateCommand(Action execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        this.execute = execute;
    }

    /// <summary>Never raised: the command can always run.</summary>
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
