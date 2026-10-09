namespace HolidayLights.App.Settings.Undo;

/// <summary>What Cancel does with a recorded file operation (PRODUCT-SPEC 2.3).</summary>
public enum UndoCancelBehavior
{
    /// <summary>Cancel keeps it: things added, created, saved, edited, renamed or imported in the session.</summary>
    KeepOnCancel,

    /// <summary>Cancel reverts it: removals (the holding folder brings the file back) and changes to Windows settings.</summary>
    RevertOnCancel,
}

/// <summary>
/// The Undo/Redo history of one Settings window session (owner: settings-ui; PRODUCT-SPEC 2.3): up to 50 steps; settings
/// changes are recorded from <see cref="ISettingsStore.Changed"/>, file operations through <see cref="Record(UndoStep)"/>; groups;
/// "Undo All Changes Since This Window Opened". See <see cref="IUndoHistory"/>.
/// </summary>
/// <remarks>
/// <para>Settings steps keep the settings before and after the change; undoing one puts back exactly the values it changed
/// (<see cref="SettingsMerge"/>), so bookkeeping that changed in between (window placement, hints) stays. Consecutive
/// changes with the same Undo text within <see cref="CoalesceWindow"/> (dragging a slider, typing a message) become one
/// step.</para>
/// <para>Steps beyond <see cref="Capacity"/> leave the history but stay known to <see cref="UndoAll"/> and
/// <see cref="RevertForCancel"/>, which also cover them.</para>
/// <para>While a step is being undone or redone nothing new is recorded. UI thread only.</para>
/// </remarks>
public sealed class UndoHistory : IUndoHistory, IDisposable
{
    /// <summary>The most steps Undo can go back (PRODUCT-SPEC 2.3).</summary>
    public const int Capacity = 50;

    /// <summary>Changes with the same Undo text closer together than this become one step.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(1500);

    private const string FallbackDescription = "Change a setting";

    private readonly ISettingsStore settings;
    private readonly TimeProvider time;
    private readonly List<HistoryStep> undo = [];
    private readonly List<HistoryStep> redo = [];
    private readonly List<HistoryStep> dropped = [];
    private GroupBuilder? group;
    private int applying;
    private bool disposed;

    /// <summary>Starts recording for a window session.</summary>
    /// <param name="settings">The settings store whose changes become steps.</param>
    public UndoHistory(ISettingsStore settings)
        : this(settings, TimeProvider.System)
    {
    }

    /// <summary>Starts recording with an explicit clock (tests).</summary>
    /// <param name="settings">The settings store whose changes become steps.</param>
    /// <param name="time">The clock used for coalescing.</param>
    public UndoHistory(ISettingsStore settings, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        this.settings = settings;
        this.time = time;
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised after the history changed (a step was recorded, undone or redone).</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsRecording => !disposed && applying == 0;

    /// <summary>True when there is a step to undo (and no group is open).</summary>
    public bool CanUndo => undo.Count > 0 && group is null && applying == 0;

    /// <summary>True when there is a step to redo.</summary>
    public bool CanRedo => redo.Count > 0 && group is null && applying == 0;

    /// <summary>The Undo text of the next undo ("Use Candy Canes for the whole frame"), or null.</summary>
    public string? UndoDescription => undo.Count > 0 ? undo[^1].Description : null;

    /// <summary>The Undo text of the next redo, or null.</summary>
    public string? RedoDescription => redo.Count > 0 ? redo[^1].Description : null;

    /// <summary>True when anything was recorded in this session (including steps beyond the capacity).</summary>
    public bool HasChanges => undo.Count > 0 || dropped.Count > 0;

    /// <summary>The file problems of the last <see cref="RevertForCancel"/> (removed files that could not be brought back).</summary>
    public IReadOnlyList<Exception> CancelProblems { get; private set; } = [];

    /// <inheritdoc />
    public void Record(UndoStep step) => Record(step, UndoCancelBehavior.KeepOnCancel);

    /// <summary>Records a file operation and what Cancel does with it.</summary>
    /// <param name="step">The step; it has already been applied.</param>
    /// <param name="behavior">Whether Cancel reverts it.</param>
    public void Record(UndoStep step, UndoCancelBehavior behavior) => Record(step, behavior, followOnCancel: null);

    /// <summary>
    /// Records a file operation, what Cancel does with it and, for one that Cancel keeps, how the settings Cancel puts back
    /// must follow it. Cancel restores the snapshot taken when the window opened, so without this a kept rename would
    /// leave the restored calendar naming a theme that no longer exists.
    /// </summary>
    /// <param name="step">The step; it has already been applied.</param>
    /// <param name="behavior">Whether Cancel reverts it.</param>
    /// <param name="followOnCancel">Applied to the restored settings when Cancel keeps the step, or null.</param>
    public void Record(UndoStep step, UndoCancelBehavior behavior, Func<AppSettings, AppSettings>? followOnCancel)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!IsRecording)
        {
            return;
        }

        Add(step.Description, new CustomPart(step, behavior, followOnCancel), coalesce: false);
    }

    /// <summary>Changes the Undo text of the open group (a removal that only partly succeeded names what it did).</summary>
    /// <param name="description">The Undo text.</param>
    public void DescribeGroup(string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        if (group is not null)
        {
            group.Description = description;
        }
    }

    /// <inheritdoc />
    public IDisposable BeginGroup(string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(description);
        if (!IsRecording)
        {
            return NoOpDisposable.Instance;
        }

        group ??= new GroupBuilder(description);
        group.Depth++;
        return new GroupScope(this);
    }

    /// <summary>Undoes the last step.</summary>
    /// <returns>The step's Undo text, or null when there was nothing to undo.</returns>
    public string? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        HistoryStep step = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        try
        {
            Apply(() => step.Undo(settings));
        }
        catch
        {
            // The step put back what it had already undone (a file that can't be moved back): it stays the next undo.
            undo.Add(step);
            OnChanged();
            throw;
        }

        redo.Add(step);
        OnChanged();
        return step.Description;
    }

    /// <summary>Redoes the last undone step.</summary>
    /// <returns>The step's Undo text, or null when there was nothing to redo.</returns>
    public string? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        HistoryStep step = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        try
        {
            Apply(() => step.Redo(settings));
        }
        catch
        {
            redo.Add(step);
            OnChanged();
            throw;
        }

        undo.Add(step);
        OnChanged();
        return step.Description;
    }

    /// <summary>
    /// "Undo All Changes Since This Window Opened": undoes every step, additions included (Cancel without closing).
    /// "Show Lights" keeps its current state, as Cancel does. The undone steps can be redone unless the session had more
    /// than <see cref="Capacity"/> steps; then the settings go back to <paramref name="snapshot"/> as a whole.
    /// </summary>
    /// <param name="snapshot">The settings when the window opened.</param>
    public void UndoAll(AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (group is not null || applying > 0 || !HasChanges)
        {
            return;
        }

        bool lightsOn = settings.Current.Lights.On;
        try
        {
            while (CanUndo)
            {
                HistoryStep step = undo[^1];
                undo.RemoveAt(undo.Count - 1);
                try
                {
                    Apply(() => step.Undo(settings));
                }
                catch
                {
                    // Stops at a step that can't be undone (it stays whole and next in line); the caller reports it.
                    undo.Add(step);
                    throw;
                }

                redo.Add(step);
            }

            if (dropped.Count > 0)
            {
                Apply(() =>
                {
                    for (int i = dropped.Count - 1; i >= 0; i--)
                    {
                        dropped[i].UndoFiles();
                    }

                    settings.Update(s => CancelScope.Restore(s, snapshot), new SettingsChange(SettingsChangeKind.UndoRedo, "Undo all changes"));
                });
                dropped.Clear();
                redo.Clear();
            }
        }
        finally
        {
            if (settings.Current.Lights.On != lightsOn)
            {
                Apply(() => settings.Update(
                    s => s with { Lights = s.Lights with { On = lightsOn } },
                    new SettingsChange(SettingsChangeKind.UndoRedo, "Undo all changes")));
            }

            OnChanged();
        }
    }

    /// <summary>
    /// Cancel's file part: reverts every applied step that Cancel reverts (removed files come back, Windows settings are
    /// restored), newest first, and stops recording. The caller then restores the settings snapshot and passes it through
    /// the returned function, which makes it agree with the steps Cancel kept (a renamed theme keeps its new name in the
    /// calendar).
    /// </summary>
    /// <returns>The adjustment of the restored settings (the identity when no kept step needs one).</returns>
    /// <remarks>A removed file that can't be brought back does not stop Cancel: the others still come back and the
    /// problem is listed in <see cref="CancelProblems"/>.</remarks>
    public Func<AppSettings, AppSettings> RevertForCancel()
    {
        Dispose();
        HistoryStep[] applied = [.. dropped, .. undo];
        var problems = new List<Exception>();
        for (int i = applied.Length - 1; i >= 0; i--)
        {
            applied[i].UndoForCancel(problems);
        }

        CancelProblems = problems;
        Func<AppSettings, AppSettings>[] follow = [.. applied.SelectMany(step => step.FollowUpsForCancel())];
        undo.Clear();
        redo.Clear();
        dropped.Clear();
        return restored => follow.Aggregate(restored, (current, next) => next(current));
    }

    /// <summary>Stops recording (the window closed).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!IsRecording || e.Change.Kind is not (SettingsChangeKind.Edit or SettingsChangeKind.ThemeLoaded or SettingsChangeKind.Import or SettingsChangeKind.Reset))
        {
            return;
        }

        string description = string.IsNullOrWhiteSpace(e.Change.Description) ? FallbackDescription : e.Change.Description;
        Add(description, new SettingsPart(e.OldSettings, e.NewSettings), coalesce: e.Change.Kind == SettingsChangeKind.Edit);
    }

    private void Add(string description, StepPart part, bool coalesce)
    {
        if (group is not null)
        {
            group.Parts.Add(part);
            return;
        }

        DateTimeOffset now = time.GetUtcNow();
        if (coalesce && redo.Count == 0 && undo.Count > 0 && undo[^1].TryCoalesce(description, part, now))
        {
            OnChanged();
            return;
        }

        Push(new HistoryStep(description, [part], now));
    }

    private void Push(HistoryStep step)
    {
        undo.Add(step);
        redo.Clear();
        if (undo.Count > Capacity)
        {
            dropped.Add(undo[0]);
            undo.RemoveAt(0);
        }

        OnChanged();
    }

    private void EndGroup()
    {
        if (group is null || --group.Depth > 0)
        {
            return;
        }

        GroupBuilder finished = group;
        group = null;
        if (finished.Parts.Count > 0)
        {
            Push(new HistoryStep(finished.Description, finished.Parts, time.GetUtcNow()));
        }
        else
        {
            OnChanged();
        }
    }

    private void Apply(Action action)
    {
        applying++;
        try
        {
            action();
        }
        finally
        {
            applying--;
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>One part of a step: a settings change or a file operation.</summary>
    private abstract class StepPart
    {
        public abstract void Undo(ISettingsStore settings, string description);

        public abstract void Redo(ISettingsStore settings, string description);
    }

    private sealed class SettingsPart(AppSettings before, AppSettings after) : StepPart
    {
        public AppSettings Before { get; } = before;

        public AppSettings After { get; set; } = after;

        public override void Undo(ISettingsStore settings, string description) =>
            settings.Update(s => SettingsMerge.Apply(s, Before, After), new SettingsChange(SettingsChangeKind.UndoRedo, description));

        public override void Redo(ISettingsStore settings, string description) =>
            settings.Update(s => SettingsMerge.Apply(s, After, Before), new SettingsChange(SettingsChangeKind.UndoRedo, description));
    }

    private sealed class CustomPart(UndoStep step, UndoCancelBehavior behavior, Func<AppSettings, AppSettings>? followOnCancel) : StepPart
    {
        public UndoCancelBehavior Behavior { get; } = behavior;

        public UndoStep Step { get; } = step;

        public Func<AppSettings, AppSettings>? FollowOnCancel { get; } = followOnCancel;

        public override void Undo(ISettingsStore settings, string description) => Step.Undo();

        public override void Redo(ISettingsStore settings, string description) => Step.Redo();
    }

    private sealed class HistoryStep(string description, List<StepPart> parts, DateTimeOffset recordedAt)
    {
        private DateTimeOffset lastChange = recordedAt;

        public string Description { get; } = description;

        /// <summary>Undoes every part, newest first; when one fails, the parts already undone are redone, so the step stays whole.</summary>
        public void Undo(ISettingsStore settings)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                try
                {
                    parts[i].Undo(settings, Description);
                }
                catch
                {
                    for (int j = i + 1; j < parts.Count; j++)
                    {
                        parts[j].Redo(settings, Description);
                    }

                    throw;
                }
            }
        }

        /// <summary>Redoes every part in order; when one fails, the parts already redone are undone again.</summary>
        public void Redo(ISettingsStore settings)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                try
                {
                    parts[i].Redo(settings, Description);
                }
                catch
                {
                    for (int j = i - 1; j >= 0; j--)
                    {
                        parts[j].Undo(settings, Description);
                    }

                    throw;
                }
            }
        }

        /// <summary>Undoes only the file operations (settings are restored as a whole afterwards).</summary>
        public void UndoFiles()
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (parts[i] is CustomPart custom)
                {
                    custom.Step.Undo();
                }
            }
        }

        /// <summary>Undoes only the file operations that Cancel reverts; one that fails is added to <paramref name="problems"/> and the others still run.</summary>
        public void UndoForCancel(List<Exception> problems)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (parts[i] is CustomPart { Behavior: UndoCancelBehavior.RevertOnCancel } custom)
                {
                    try
                    {
                        custom.Step.Undo();
                    }
                    catch (Exception exception) when (FileProblems.Is(exception))
                    {
                        problems.Add(exception);
                    }
                }
            }
        }

        /// <summary>The settings adjustments of the file operations that Cancel keeps, in the order they were applied.</summary>
        public IEnumerable<Func<AppSettings, AppSettings>> FollowUpsForCancel() =>
            parts.OfType<CustomPart>()
                .Where(part => part.Behavior == UndoCancelBehavior.KeepOnCancel && part.FollowOnCancel is not null)
                .Select(part => part.FollowOnCancel!);

        /// <summary>Merges a settings change with the same text into this settings-only step when it follows closely.</summary>
        public bool TryCoalesce(string description, StepPart part, DateTimeOffset now)
        {
            if (part is not SettingsPart next || parts is not [SettingsPart last] || description != Description
                || now - lastChange > CoalesceWindow || !ReferenceEquals(last.After, next.Before))
            {
                return false;
            }

            last.After = next.After;
            lastChange = now;
            return true;
        }
    }

    private sealed class GroupBuilder(string description)
    {
        public string Description { get; set; } = description;

        public List<StepPart> Parts { get; } = [];

        public int Depth { get; set; }
    }

    private sealed class GroupScope(UndoHistory history) : IDisposable
    {
        private bool closed;

        public void Dispose()
        {
            if (closed)
            {
                return;
            }

            closed = true;
            history.EndGroup();
        }
    }
}
