namespace HolidayLights.Core.Abstractions;

/// <summary>Why the settings changed (lets the scene builder animate, and the undo history skip its own changes).</summary>
public enum SettingsChangeKind
{
    /// <summary>A user edit in a window, the tray or a hot key (recorded by Undo while Settings is open).</summary>
    Edit,

    /// <summary>A theme was loaded by hand (theme transition, Recent Settings recorded).</summary>
    ThemeLoaded,

    /// <summary>Automatic themes switched the theme at a boundary.</summary>
    AutomaticTheme,

    /// <summary>Undo or Redo applied a recorded state (not recorded again).</summary>
    UndoRedo,

    /// <summary>Cancel restored the snapshot (not recorded).</summary>
    CancelRestore,

    /// <summary>The Holiday Lights 5.4 import or "Start Fresh Instead" / "Use My 2003 Lights".</summary>
    Import,

    /// <summary>"Reset All Settings".</summary>
    Reset,

    /// <summary>Bookkeeping that is not a user choice (window placement, last page, onboarding flags); never recorded by Undo.</summary>
    Internal,
}

/// <summary>Describes one change.</summary>
/// <param name="Kind">Why the settings changed.</param>
/// <param name="Description">The Undo text without the "Undo: " prefix (e.g. "Use Candy Canes for the whole frame"); null for internal changes.</param>
public sealed record SettingsChange(SettingsChangeKind Kind, string? Description = null)
{
    /// <summary>A user edit with its Undo text.</summary>
    /// <param name="description">The Undo text, e.g. "Use Candy Canes for the whole frame".</param>
    /// <returns>The change.</returns>
    public static SettingsChange Edit(string description) => new(SettingsChangeKind.Edit, description);

    /// <summary>Bookkeeping change, never recorded by Undo.</summary>
    public static SettingsChange Internal { get; } = new(SettingsChangeKind.Internal);
}

/// <summary>Arguments of <see cref="ISettingsStore.Changed"/>.</summary>
public sealed class SettingsChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="oldSettings">The settings before the change.</param>
    /// <param name="newSettings">The settings after the change.</param>
    /// <param name="change">What kind of change it was.</param>
    public SettingsChangedEventArgs(AppSettings oldSettings, AppSettings newSettings, SettingsChange change)
    {
        OldSettings = oldSettings;
        NewSettings = newSettings;
        Change = change;
    }

    /// <summary>The settings before the change.</summary>
    public AppSettings OldSettings { get; }

    /// <summary>The settings after the change.</summary>
    public AppSettings NewSettings { get; }

    /// <summary>What kind of change it was.</summary>
    public SettingsChange Change { get; }
}

/// <summary>How the settings file was found at start-up (PRODUCT-SPEC 5.13).</summary>
public enum SettingsLoadOutcome
{
    /// <summary>The file existed and was read.</summary>
    Loaded,

    /// <summary>There was no file: first run (newcomer or 5.4 import follows).</summary>
    Created,

    /// <summary>The file was unreadable: renamed to <c>settings.damaged-&lt;date&gt;.json</c>; defaults are used ("Settings were reset").</summary>
    ReplacedDamaged,

    /// <summary>
    /// The settings file exists but could not be read (held by another program, being replaced, access denied): the defaults
    /// are used, the file is neither renamed nor written, and its settings replace the defaults (<see cref="ISettingsStore.Changed"/>,
    /// <see cref="SettingsChangeKind.Internal"/>) as soon as it can be read (review r1 #6).
    /// </summary>
    Unavailable,
}

/// <summary>Arguments of <see cref="ISettingsStore.SaveFailed"/>.</summary>
public sealed class SettingsSaveFailedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="exception">The I/O error.</param>
    public SettingsSaveFailedEventArgs(Exception exception) => Exception = exception;

    /// <summary>The I/O error; its message is the "Windows reason" of "Couldn't save your settings: &lt;reason&gt;.".</summary>
    public Exception Exception { get; }
}

/// <summary>
/// The single source of truth for settings (ARCHITECTURE 3: only the UI thread mutates settings). Implemented by
/// core-settings.
/// </summary>
/// <remarks>
/// <para>Every change applies at once: <see cref="Changed"/> is raised synchronously on the calling (UI) thread, then the
/// file is written atomically (temporary file + replace) after a 500 ms debounce. A failed write keeps the change in memory,
/// raises <see cref="SaveFailed"/> and is retried with the next change.</para>
/// <para>Readers on other threads use <see cref="Current"/> (an immutable snapshot, safe to read anywhere).</para>
/// </remarks>
public interface ISettingsStore
{
    /// <summary>The current settings (immutable snapshot).</summary>
    AppSettings Current { get; }

    /// <summary>How the file was found at start-up.</summary>
    SettingsLoadOutcome LoadOutcome { get; }

    /// <summary>Raised on the UI thread after every change.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>Raised when writing the file failed.</summary>
    event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed;

    /// <summary>Applies a change (UI thread). A transform that returns the same instance is not a change.</summary>
    /// <param name="transform">Returns the new settings from the current ones.</param>
    /// <param name="change">What kind of change it is (and its Undo text).</param>
    void Update(Func<AppSettings, AppSettings> transform, SettingsChange change);

    /// <summary>Writes pending changes now (exit, uninstall, before a settings-only session ends).</summary>
    /// <returns>A task that completes when the file is written.</returns>
    Task FlushAsync();
}
