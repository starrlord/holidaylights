namespace HolidayLights.Core.Abstractions;

/// <summary>A custom undo step (file operations); settings changes are recorded automatically.</summary>
/// <param name="Description">The Undo text without the "Undo: " prefix, e.g. "Remove Star".</param>
/// <param name="Undo">Reverts the step (UI thread).</param>
/// <param name="Redo">Applies the step again (UI thread).</param>
public sealed record UndoStep(string Description, Action Undo, Action Redo);

/// <summary>
/// The Settings window's Undo/Redo history (PRODUCT-SPEC 2.3: up to 50 steps for the life of the window). Implemented by
/// settings-ui; a no-op history is active while the window is closed. UI thread only.
/// </summary>
/// <remarks>
/// <para>While recording, every <see cref="ISettingsStore"/> change of kind <see cref="SettingsChangeKind.Edit"/>,
/// <see cref="SettingsChangeKind.ThemeLoaded"/>, <see cref="SettingsChangeKind.Import"/> or
/// <see cref="SettingsChangeKind.Reset"/> becomes one step automatically (its description is the Undo text), including
/// changes from the tray and hot keys.</para>
/// <para>File operations record their own <see cref="UndoStep"/>. Wrap a file operation and the settings changes that
/// belong to it in <see cref="BeginGroup"/> so they undo as one step (e.g. "Remove Bulb": hold the file + update the
/// arrangement).</para>
/// </remarks>
public interface IUndoHistory
{
    /// <summary>True while the Settings window is open and steps are recorded.</summary>
    bool IsRecording { get; }

    /// <summary>Records a custom step (no-op when not recording).</summary>
    /// <param name="step">The step; it has already been applied.</param>
    void Record(UndoStep step);

    /// <summary>Starts a group: everything recorded until the returned object is disposed becomes one step.</summary>
    /// <param name="description">The Undo text of the group.</param>
    /// <returns>Dispose to close the group.</returns>
    IDisposable BeginGroup(string description);
}
