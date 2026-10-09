namespace HolidayLights.App.Settings.Undo;

/// <summary>The history while the Settings window is closed: records nothing (<see cref="IAppServices.Undo"/>).</summary>
public sealed class NullUndoHistory : IUndoHistory
{
    /// <summary>The shared instance.</summary>
    public static NullUndoHistory Instance { get; } = new();

    private NullUndoHistory()
    {
    }

    /// <inheritdoc />
    public bool IsRecording => false;

    /// <inheritdoc />
    public void Record(UndoStep step)
    {
    }

    /// <inheritdoc />
    public IDisposable BeginGroup(string description) => NoOpDisposable.Instance;
}

/// <summary>A disposable that does nothing.</summary>
internal sealed class NoOpDisposable : IDisposable
{
    /// <summary>The shared instance.</summary>
    public static NoOpDisposable Instance { get; } = new();

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
