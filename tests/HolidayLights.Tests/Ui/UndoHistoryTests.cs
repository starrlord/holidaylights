using HolidayLights.App.Settings.Undo;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Ui;

/// <summary>The Undo/Redo history of the Settings window (PRODUCT-SPEC 2.3).</summary>
public sealed class UndoHistoryTests
{
    private readonly InMemorySettingsStore store = new();
    private readonly ManualTime time = new();

    [Fact]
    public void EditsBecomeStepsThatUndoAndRedo()
    {
        using var history = new UndoHistory(store, time);
        SetPattern(FlashPatternId.Twinkle, "Change the flash pattern to Twinkle");

        Assert.True(history.CanUndo);
        Assert.Equal("Change the flash pattern to Twinkle", history.UndoDescription);

        Assert.Equal("Change the flash pattern to Twinkle", history.Undo());
        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
        Assert.Equal(SettingsChangeKind.UndoRedo, store.History[^1].Kind);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Equal(FlashPatternId.Twinkle, store.Current.Current.Flash.Pattern);
        Assert.False(history.CanRedo);
    }

    [Theory]
    [InlineData(SettingsChangeKind.UndoRedo)]
    [InlineData(SettingsChangeKind.CancelRestore)]
    [InlineData(SettingsChangeKind.Internal)]
    [InlineData(SettingsChangeKind.AutomaticTheme)]
    public void BookkeepingChangesAreNotRecorded(SettingsChangeKind kind)
    {
        using var history = new UndoHistory(store, time);
        store.Update(s => s with { Look = s.Look with { Glow = GlowLevel.Bright } }, new SettingsChange(kind, "x"));

        Assert.False(history.CanUndo);
    }

    [Theory]
    [InlineData(SettingsChangeKind.ThemeLoaded)]
    [InlineData(SettingsChangeKind.Import)]
    [InlineData(SettingsChangeKind.Reset)]
    public void ThemeImportAndResetChangesAreRecorded(SettingsChangeKind kind)
    {
        using var history = new UndoHistory(store, time);
        store.Update(s => s with { Look = s.Look with { Glow = GlowLevel.Bright } }, new SettingsChange(kind, "Load Halloween"));

        Assert.Equal("Load Halloween", history.UndoDescription);
    }

    [Fact]
    public void UndoKeepsBookkeepingThatChangedInBetween()
    {
        using var history = new UndoHistory(store, time);
        SetPattern(FlashPatternId.SlowGlow, "Change the flash pattern to Slow Glow");
        store.Update(s => s with { Ui = s.Ui with { Settings = s.Ui.Settings with { LastPage = SettingsPageId.General } } }, SettingsChange.Internal);

        history.Undo();

        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
        Assert.Equal(SettingsPageId.General, store.Current.Ui.Settings.LastPage);
    }

    [Fact]
    public void ConsecutiveChangesWithTheSameTextCoalesce()
    {
        using var history = new UndoHistory(store, time);
        for (int interval = 4; interval >= 1; interval--)
        {
            int value = interval;
            store.Update(s => s with { Current = s.Current with { Flash = s.Current.Flash with { Interval = value } } }, SettingsChange.Edit("Change the flash speed"));
            time.Advance(TimeSpan.FromMilliseconds(200));
        }

        history.Undo();

        Assert.Equal(FlashSettings.DefaultInterval, store.Current.Current.Flash.Interval);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ChangesFarApartStaySeparate()
    {
        using var history = new UndoHistory(store, time);
        store.Update(s => s with { Current = s.Current with { Flash = s.Current.Flash with { Interval = 4 } } }, SettingsChange.Edit("Change the flash speed"));
        time.Advance(UndoHistory.CoalesceWindow + TimeSpan.FromMilliseconds(1));
        store.Update(s => s with { Current = s.Current with { Flash = s.Current.Flash with { Interval = 3 } } }, SettingsChange.Edit("Change the flash speed"));

        history.Undo();

        Assert.Equal(4, store.Current.Current.Flash.Interval);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void GroupsUndoFilesAndSettingsTogether()
    {
        using var history = new UndoHistory(store, time);
        var log = new List<string>();
        using (history.BeginGroup("Remove Star"))
        {
            history.Record(new UndoStep("Remove Star", () => log.Add("restore file"), () => log.Add("hold file")));
            SetPattern(FlashPatternId.BulbChase, "inner");
        }

        Assert.Equal("Remove Star", history.UndoDescription);
        history.Undo();

        Assert.Equal(["restore file"], log);
        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
        history.Redo();
        Assert.Equal(["restore file", "hold file"], log);
        Assert.Equal(FlashPatternId.BulbChase, store.Current.Current.Flash.Pattern);
    }

    [Fact]
    public void NestedGroupsBecomeOneStep()
    {
        using var history = new UndoHistory(store, time);
        using (history.BeginGroup("Outer"))
        {
            using (history.BeginGroup("Inner"))
            {
                SetPattern(FlashPatternId.Alternating, "a");
            }

            SetPattern(FlashPatternId.BulbChase, "b");
        }

        Assert.Equal("Outer", history.UndoDescription);
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
    }

    [Fact]
    public void NothingIsRecordedWhileUndoRuns()
    {
        using var history = new UndoHistory(store, time);
        history.Record(new UndoStep("Remove Star", () => SetPattern(FlashPatternId.Twinkle, "side effect"), () => { }));

        history.Undo();

        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void TheHistoryKeepsFiftySteps()
    {
        using var history = new UndoHistory(store, time);
        for (int i = 0; i < UndoHistory.Capacity + 5; i++)
        {
            history.Record(new UndoStep($"Step {i}", () => { }, () => { }));
        }

        int undone = 0;
        while (history.Undo() is not null)
        {
            undone++;
        }

        Assert.Equal(UndoHistory.Capacity, undone);
    }

    [Fact]
    public void UndoAllRestoresTheSnapshotButKeepsShowLights()
    {
        AppSettings snapshot = store.Current;
        using var history = new UndoHistory(store, time);
        var undone = new List<string>();
        history.Record(new UndoStep("Add Star", () => undone.Add("Add Star"), () => { }));
        SetPattern(FlashPatternId.Twinkle, "pattern");
        store.Update(s => s with { Lights = s.Lights with { On = false } }, SettingsChange.Edit("Turn the lights off"));

        history.UndoAll(snapshot);

        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
        Assert.False(store.Current.Lights.On);
        Assert.Equal(["Add Star"], undone);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void UndoAllCoversStepsBeyondTheCapacity()
    {
        AppSettings snapshot = store.Current;
        using var history = new UndoHistory(store, time);
        var undone = new List<int>();
        SetPattern(FlashPatternId.Twinkle, "first");
        for (int i = 0; i < UndoHistory.Capacity + 3; i++)
        {
            int number = i;
            history.Record(new UndoStep($"Step {i}", () => undone.Add(number), () => { }));
        }

        history.UndoAll(snapshot);

        Assert.Equal(UndoHistory.Capacity + 3, undone.Count);
        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void CancelRevertsRemovalsButKeepsAdditions()
    {
        using var history = new UndoHistory(store, time);
        var log = new List<string>();
        history.Record(new UndoStep("Add Star", () => log.Add("undo add"), () => { }), UndoCancelBehavior.KeepOnCancel);
        history.Record(new UndoStep("Remove Moon", () => log.Add("bring back Moon"), () => { }), UndoCancelBehavior.RevertOnCancel);
        history.Record(new UndoStep("Remove Sun", () => log.Add("bring back Sun"), () => { }), UndoCancelBehavior.RevertOnCancel);

        history.RevertForCancel();

        Assert.Equal(["bring back Sun", "bring back Moon"], log);
        Assert.False(history.IsRecording);
    }

    [Fact]
    public void CancelMakesTheRestoredSettingsFollowTheKeptStepsInOrder()
    {
        using var history = new UndoHistory(store, time);
        AppSettings Rename(AppSettings s, string from, string to) =>
            s.Calendar.Between == from ? s with { Calendar = s.Calendar with { Between = to } } : s;
        history.Record(new UndoStep("Rename A to B", () => { }, () => { }), UndoCancelBehavior.KeepOnCancel, s => Rename(s, "A", "B"));
        history.Record(new UndoStep("Rename B to C", () => { }, () => { }), UndoCancelBehavior.KeepOnCancel, s => Rename(s, "B", "C"));
        history.Record(new UndoStep("Rename C to D", () => { }, () => { }), UndoCancelBehavior.KeepOnCancel, s => Rename(s, "C", "D"));
        history.Undo();
        history.Record(new UndoStep("Remove E", () => { }, () => { }), UndoCancelBehavior.RevertOnCancel, s => Rename(s, "C", "E"));

        Func<AppSettings, AppSettings> follow = history.RevertForCancel();

        // The undone rename and the reverted removal leave the restored settings alone.
        AppSettings snapshot = new AppSettings() with { Calendar = new CalendarSettings { Between = "A" } };
        Assert.Equal("C", follow(snapshot).Calendar.Between);
        AppSettings untouched = new AppSettings() with { Calendar = new CalendarSettings { Between = "Z" } };
        Assert.Same(untouched, follow(untouched));
    }

    [Fact]
    public void AStepThatFailsToUndoStaysWholeAndNextInLine()
    {
        using var history = new UndoHistory(store, time);
        bool locked = true;
        using (history.BeginGroup("Rename A to B"))
        {
            history.Record(new UndoStep("Rename A to B", () =>
            {
                if (locked)
                {
                    throw new IOException("The file is in use.");
                }
            }, () => { }));
            SetPattern(FlashPatternId.Twinkle, "Rename A to B");
        }

        Assert.Throws<IOException>(() => history.Undo());
        Assert.Equal(FlashPatternId.Twinkle, store.Current.Current.Flash.Pattern);
        Assert.Equal("Rename A to B", history.UndoDescription);
        Assert.False(history.CanRedo);

        locked = false;
        Assert.Equal("Rename A to B", history.Undo());
        Assert.Equal(FlashPatternId.FlashTogether, store.Current.Current.Flash.Pattern);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void CancelBringsBackWhatItCanWhenOneRemovalFails()
    {
        using var history = new UndoHistory(store, time);
        var log = new List<string>();
        history.Record(new UndoStep("Remove Moon", () => log.Add("bring back Moon"), () => { }), UndoCancelBehavior.RevertOnCancel);
        history.Record(new UndoStep("Remove Sun", () => throw new IOException("The file is in use."), () => { }), UndoCancelBehavior.RevertOnCancel);

        history.RevertForCancel();

        Assert.Equal(["bring back Moon"], log);
        Assert.IsType<IOException>(Assert.Single(history.CancelProblems));
    }

    [Fact]
    public void AGroupCanBeRenamedBeforeItCloses()
    {
        using var history = new UndoHistory(store, time);
        using (history.BeginGroup("Remove 2 bulbs"))
        {
            history.Record(new UndoStep("Remove Star", () => { }, () => { }));
            history.DescribeGroup("Remove Star");
        }

        Assert.Equal("Remove Star", history.UndoDescription);
    }

    [Fact]
    public void ARemovalUndoneByTheUserIsNotRevertedAgainByCancel()
    {
        using var history = new UndoHistory(store, time);
        int restores = 0;
        history.Record(new UndoStep("Remove Moon", () => restores++, () => { }), UndoCancelBehavior.RevertOnCancel);
        history.Undo();

        history.RevertForCancel();

        Assert.Equal(1, restores);
    }

    [Fact]
    public void ANewStepClearsRedo()
    {
        using var history = new UndoHistory(store, time);
        SetPattern(FlashPatternId.Twinkle, "a");
        history.Undo();
        SetPattern(FlashPatternId.BulbChase, "b");

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void DisposeStopsRecording()
    {
        var history = new UndoHistory(store, time);
        history.Dispose();
        SetPattern(FlashPatternId.Twinkle, "a");

        Assert.False(history.CanUndo);
        Assert.False(history.IsRecording);
    }

    [Fact]
    public void TheClosedWindowHistoryRecordsNothing()
    {
        IUndoHistory history = NullUndoHistory.Instance;
        history.Record(new UndoStep("x", () => { }, () => { }));
        using IDisposable group = history.BeginGroup("y");

        Assert.False(history.IsRecording);
    }

    private void SetPattern(FlashPatternId pattern, string description) =>
        store.Update(s => s with { Current = s.Current with { Flash = s.Current.Flash with { Pattern = pattern } } }, SettingsChange.Edit(description));

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}
