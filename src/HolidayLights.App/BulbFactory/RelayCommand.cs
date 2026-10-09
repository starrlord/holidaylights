using System.Windows.Input;

namespace HolidayLights.App.BulbFactory;

/// <summary>A command that runs a delegate; <see cref="Refresh"/> re-evaluates whether it can run.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool> canExecute;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">What it does.</param>
    /// <param name="canExecute">Whether it is enabled (always, when null).</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute ?? (() => true);
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => canExecute();

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    /// <summary>Raises <see cref="CanExecuteChanged"/>.</summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A command that runs an asynchronous delegate and is disabled while it runs.</summary>
internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool> canExecute;
    private bool running;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">What it does.</param>
    /// <param name="canExecute">Whether it is enabled (always, when null).</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute ?? (() => true);
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !running && canExecute();

    /// <inheritdoc />
    /// <remarks>Exceptions of the delegate are not expected (view models turn failures into messages); they would surface on the dispatcher.</remarks>
    public async void Execute(object? parameter) => await ExecuteAsync();

    /// <summary>Runs the command if it can run (also what tests await).</summary>
    /// <returns>A task that completes when the delegate finished.</returns>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        running = true;
        Refresh();
        try
        {
            await execute();
        }
        finally
        {
            running = false;
            Refresh();
        }
    }

    /// <summary>Raises <see cref="CanExecuteChanged"/>.</summary>
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
