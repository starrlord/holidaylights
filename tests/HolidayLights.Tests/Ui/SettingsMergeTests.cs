using HolidayLights.App.Settings.Undo;

namespace HolidayLights.Tests.Ui;

/// <summary>The three-way merge behind Undo, Redo and Cancel.</summary>
public sealed class SettingsMergeTests
{
    [Fact]
    public void OnlyTheChangedValuesGoBack()
    {
        var before = new AppSettings();
        AppSettings after = before with { Look = before.Look with { Glow = GlowLevel.Bright } };
        AppSettings current = after with { Music = after.Music with { Volume = 80 } };

        AppSettings merged = SettingsMerge.Apply(current, before, after);

        Assert.Equal(GlowLevel.Soft, merged.Look.Glow);
        Assert.Equal(80, merged.Music.Volume);
    }

    [Fact]
    public void NestedRecordsMergeFieldByField()
    {
        var before = new AppSettings();
        AppSettings after = before with { Current = before.Current with { Flash = before.Current.Flash with { Pattern = FlashPatternId.Twinkle } } };
        AppSettings current = after with { Current = after.Current with { Flash = after.Current.Flash with { Interval = 2 } } };

        AppSettings merged = SettingsMerge.Apply(current, before, after);

        Assert.Equal(FlashPatternId.FlashTogether, merged.Current.Flash.Pattern);
        Assert.Equal(2, merged.Current.Flash.Interval);
    }

    [Fact]
    public void TheArrangementChangesAsAWhole()
    {
        var before = new AppSettings();
        SlotAssignment candy = SlotAssignment.Empty.WithEdge(Side.Top, ["builtin:candy-canes"]);
        AppSettings after = before with { Current = before.Current with { Arrangement = candy } };

        AppSettings merged = SettingsMerge.Apply(after, before, after);

        Assert.Equal(SlotAssignment.Classic54Default, merged.Current.Arrangement);
    }

    [Fact]
    public void NothingChangedReturnsTheSameInstance()
    {
        var before = new AppSettings();
        AppSettings after = before with { Look = before.Look with { Glow = GlowLevel.Bright } };
        AppSettings current = before with { Music = before.Music with { Volume = 10 } };

        Assert.Same(current, SettingsMerge.Apply(current, after, after));
    }

    [Fact]
    public void PublishedInstancesAreNeverModified()
    {
        var before = new AppSettings();
        AppSettings after = before with { Look = before.Look with { Glow = GlowLevel.Bright } };

        _ = SettingsMerge.Apply(after, before, after);

        Assert.Equal(GlowLevel.Bright, after.Look.Glow);
    }

    [Fact]
    public void EqualListsCountAsUnchanged()
    {
        Assert.True(SettingsMerge.ValuesEqual(new[] { "a", "b" }, new List<string> { "a", "b" }));
        Assert.False(SettingsMerge.ValuesEqual(new[] { "a", "b" }, new[] { "b", "a" }));
        Assert.True(SettingsMerge.ValuesEqual(
            new Dictionary<string, IReadOnlyList<string>> { ["x"] = ["Halloween"] },
            new Dictionary<string, IReadOnlyList<string>> { ["x"] = ["Halloween"] }));
    }

    [Fact]
    public void CancelRestoresTheSnapshotButNotShowLightsOrBookkeeping()
    {
        var snapshot = new AppSettings();
        AppSettings current = snapshot with
        {
            Lights = snapshot.Lights with { On = false, Drawing = BulbDrawing.OnTop },
            Current = snapshot.Current with { Flash = new FlashSettings { Pattern = FlashPatternId.BulbChase, Interval = 2 } },
            Colors = new ColorSettings { Custom = [.. Enumerable.Repeat(RgbColor.White, 16)] },
            Ui = snapshot.Ui with { DecorateWindow = false, Settings = new SettingsWindowSettings { LastPage = SettingsPageId.Themes } },
            Onboarding = snapshot.Onboarding with { CloseNotified = true },
        };

        AppSettings restored = CancelScope.Restore(current, snapshot);

        Assert.False(restored.Lights.On);
        Assert.Equal(BulbDrawing.Desktop, restored.Lights.Drawing);
        Assert.Equal(FlashPatternId.FlashTogether, restored.Current.Flash.Pattern);
        Assert.Equal(FlashSettings.DefaultInterval, restored.Current.Flash.Interval);
        Assert.All(restored.Colors.Custom, c => Assert.Equal(RgbColor.Black, c));
        Assert.True(restored.Ui.DecorateWindow);
        Assert.Equal(SettingsPageId.Themes, restored.Ui.Settings.LastPage);
        Assert.True(restored.Onboarding.CloseNotified);
    }
}
