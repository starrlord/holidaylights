using HolidayLights.App.Shell;
using HolidayLights.Core.Layout;
using HolidayLights.Tests.App.Fakes;

namespace HolidayLights.Tests.App;

/// <summary>
/// The scene builder (CONTRACTS 6.2): displays, work areas and scales into layout targets, the effective pattern, interval
/// and look under the flash limit and the Energy Saver rules, seeds and transitions (PRODUCT-SPEC 5.2, 5.5.3, 5.8, 5.9,
/// 5.12, 4.3).
/// </summary>
public sealed class SceneBuilderTests
{
    private static readonly string StandardBulbs = BulbIds.BuiltIn("standard-bulbs");

    private int seedCount;

    private SceneBuilder Builder() => new(new ClassicLayoutEngine(), new BuiltInResolver(), () => new SceneSeeds((ulong)++seedCount, (uint)seedCount));

    private static AppSettings Settings(Func<AppSettings, AppSettings>? change = null)
    {
        var settings = new AppSettings();
        return change is null ? settings : change(settings);
    }

    private static SceneInputs Inputs(AppSettings settings, bool energySaver = false, bool animations = true, IReadOnlyList<DisplayInfo>? displays = null) =>
        new(settings, displays ?? FakeDisplayService.ReferencePc(), energySaver, animations);

    private static SlotAssignment StandardEverywhere() => new()
    {
        Top = [StandardBulbs],
        Right = [StandardBulbs],
        Bottom = [StandardBulbs],
        Left = [StandardBulbs],
        TopLeft = StandardBulbs,
        TopRight = StandardBulbs,
        BottomRight = StandardBulbs,
        BottomLeft = StandardBulbs,
    };

    [Fact]
    public void TheReferencePcGetsTwoCompleteFramesAtScaleOneAndAHalf()
    {
        AppSettings settings = Settings(s => s with { Current = s.Current with { Arrangement = StandardEverywhere() } });
        LightsScene scene = Builder().Build(Inputs(settings));

        Assert.Equal(2, scene.Displays.Count);
        Assert.All(scene.Displays, d => Assert.True(d.Enabled));
        Assert.Equal(2, scene.Layout.Displays.Count);
        foreach (DisplayLayout display in scene.Layout.Displays)
        {
            Assert.Equal(1.5, display.Target.Scale);
            Assert.Equal(3840, display.Target.Area.Width);
            Assert.Equal(2088, display.Target.Area.Height);

            // PRODUCT-SPEC 5.3.2: Standard Bulbs are 48 x 48 px; the top strip holds 78 bulbs between two 48 px corners.
            StripLayout top = display.Strips.First(s => s.Side == Side.Top);
            Assert.Equal(78, top.Count);
            Assert.All(top.Placements, p => Assert.Equal(48, p.Bounds.Height));
            Assert.Equal(2088, display.Strips.Max(s => s.Rect.Bottom) - display.Strips.Min(s => s.Rect.Top));
        }

        DisplayLayout second = scene.Layout.Displays.Single(d => d.Target.DisplayId == "display-2");
        Assert.Equal(-3840, second.Target.Area.Left);
    }

    [Fact]
    public void BulbSizeMultipliesTheDisplayScale()
    {
        LightsScene scene = Builder().Build(Inputs(Settings(s => s with { Lights = s.Lights with { Size = BulbSize.Large } })));
        Assert.All(scene.Layout.Displays, d => Assert.Equal(2.25, d.Target.Scale));
    }

    [Fact]
    public void DisabledDisplaysGetNoLightsButAtLeastOneDisplayStaysOn()
    {
        AppSettings oneOff = Settings(s => s with { Lights = s.Lights with { Displays = new DisplaySelection { Disabled = ["display-2"] } } });
        LightsScene scene = Builder().Build(Inputs(oneOff));
        Assert.Equal([true, false], scene.Displays.Select(d => d.Enabled));
        Assert.Single(scene.Layout.Displays);

        AppSettings allOff = Settings(s => s with { Lights = s.Lights with { Displays = new DisplaySelection { Disabled = ["display-1", "display-2"] } } });
        LightsScene fallback = Builder().Build(Inputs(allOff));
        Assert.Equal("display-1", Assert.Single(fallback.Layout.Displays).Target.DisplayId);
        Assert.True(fallback.Displays.Single(d => d.Display.IsPrimary).Enabled);
    }

    [Fact]
    public void ANewlyConnectedDisplayIsOn()
    {
        DisplayInfo[] three = [.. FakeDisplayService.ReferencePc(), FakeDisplayService.Display(3, 3840, primary: false, dpi: 96, width: 1920, height: 1080, taskbar: 48)];
        LightsScene scene = Builder().Build(Inputs(Settings(), displays: three));
        Assert.Equal(3, scene.Layout.Displays.Count);
        Assert.Equal(1.0, scene.Layout.Displays.Single(d => d.Target.DisplayId == "display-3").Target.Scale);
    }

    [Fact]
    public void TheFrameModeReachesTheLayout()
    {
        LightsScene scene = Builder().Build(Inputs(Settings(s => s with { Lights = s.Lights with { FrameMode = FrameMode.AllDisplaysTogether } })));
        Assert.Equal(FrameMode.AllDisplaysTogether, scene.Layout.Mode);
    }

    [Theory]
    [InlineData(BulbDrawing.Desktop, true, LayerMode.BehindIcons)]
    [InlineData(BulbDrawing.Desktop, false, LayerMode.InFrontOfIcons)]
    [InlineData(BulbDrawing.OnTop, true, LayerMode.OnTop)]
    public void BulbDrawingChoosesTheLayer(BulbDrawing drawing, bool behind, LayerMode expected) =>
        Assert.Equal(expected, Builder().Build(Inputs(Settings(s => s with { Lights = s.Lights with { Drawing = drawing, BehindIcons = behind } }))).RequestedLayer);

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 3)]
    [InlineData(2, true, 3)]
    [InlineData(5, true, 5)]
    [InlineData(9, true, 9)]
    public void TheFlashLimitSlowsFastSpeeds(int interval, bool limit, int expected)
    {
        AppSettings settings = Settings(s => s with
        {
            Current = s.Current with { Flash = new FlashSettings { Interval = interval } },
            Accessibility = new AccessibilitySettings { LimitFlashing = limit },
        });
        LightsScene scene = Builder().Build(Inputs(settings));
        Assert.Equal(expected, scene.Interval);
        Assert.Equal(limit, scene.Flash.LimitFlashing);
        if (limit)
        {
            Assert.True(scene.Flash.SmoothFading);
        }
    }

    [Fact]
    public void SmoothFadingIsForcedOnByTheFlashLimit()
    {
        AppSettings settings = Settings(s => s with
        {
            Look = s.Look with { SmoothFading = false },
            Accessibility = new AccessibilitySettings { LimitFlashing = true },
        });
        Assert.True(Builder().Build(Inputs(settings)).Flash.SmoothFading);
        Assert.False(Builder().Build(Inputs(settings with { Accessibility = new AccessibilitySettings() })).Flash.SmoothFading);
    }

    [Fact]
    public void UseLessPowerKeepsFlashingSlowerWithoutGlowOrFading()
    {
        AppSettings settings = Settings(s => s with { Current = s.Current with { Flash = new FlashSettings { Interval = 2 } } });
        LightsScene scene = Builder().Build(Inputs(settings, energySaver: true));
        Assert.Equal(5, scene.Interval);
        Assert.False(scene.Flash.SmoothFading);
        Assert.False(scene.Flash.StopFlashing);
        Assert.Equal(GlowLevel.Off, scene.Effects.Glow);
        Assert.True(scene.LightsOn);
        Assert.Equal(EnergySaverChoice.UseLessPower, scene.EnergySaverInEffect);

        LightsScene normal = Builder().Build(Inputs(settings, energySaver: false));
        Assert.Equal(2, normal.Interval);
        Assert.Equal(GlowLevel.Soft, normal.Effects.Glow);
        Assert.Null(normal.EnergySaverInEffect);
    }

    [Fact]
    public void StopFlashingFreezesTheLightsWithoutGlow()
    {
        AppSettings settings = Settings(s => s with { Rest = s.Rest with { EnergySaver = EnergySaverChoice.StopFlashing } });
        LightsScene scene = Builder().Build(Inputs(settings, energySaver: true));
        Assert.True(scene.Flash.StopFlashing);
        Assert.False(scene.Flash.SmoothFading);
        Assert.Equal(GlowLevel.Off, scene.Effects.Glow);
        Assert.True(scene.LightsOn);
    }

    [Fact]
    public void TurnOffTheLightsHidesThemUntilEnergySaverEnds()
    {
        AppSettings settings = Settings(s => s with { Rest = s.Rest with { EnergySaver = EnergySaverChoice.TurnOffLights } });
        Assert.False(Builder().Build(Inputs(settings, energySaver: true)).LightsOn);
        Assert.True(Builder().Build(Inputs(settings, energySaver: false)).LightsOn);
    }

    [Fact]
    public void ChangeNothingChangesNothing()
    {
        AppSettings settings = Settings(s => s with { Rest = s.Rest with { EnergySaver = EnergySaverChoice.ChangeNothing } });
        LightsScene scene = Builder().Build(Inputs(settings, energySaver: true));
        Assert.Null(scene.EnergySaverInEffect);
        Assert.Equal(GlowLevel.Soft, scene.Effects.Glow);
        Assert.Equal(5, scene.Interval);
    }

    [Fact]
    public void ShowLightsOffTurnsTheSceneOff() =>
        Assert.False(Builder().Build(Inputs(Settings(s => s with { Lights = s.Lights with { On = false } }))).LightsOn);

    [Fact]
    public void ReducedMotionFollowsWindowsAnimationEffects()
    {
        Assert.True(Builder().Build(Inputs(Settings(), animations: false)).Effects.ReducedMotion);
        Assert.False(Builder().Build(Inputs(Settings(), animations: true)).Effects.ReducedMotion);
    }

    [Fact]
    public void TheLookReachesTheEffects()
    {
        AppSettings classic = Settings(s => s with { Look = LookPresets.ValuesOf(LookPreset.Classic2003)! });
        LightsScene scene = Builder().Build(Inputs(classic));
        Assert.Equal(SpriteStyle.Crisp, scene.Effects.Pixels);
        Assert.Equal(GlowLevel.Off, scene.Effects.Glow);
        Assert.False(scene.Flash.SmoothFading);
    }

    [Fact]
    public void AnUnchangedBuildKeepsTheSceneAndItsSeeds()
    {
        SceneBuilder builder = Builder();
        AppSettings settings = Settings();
        LightsScene first = builder.Build(Inputs(settings));
        LightsScene again = builder.Build(Inputs(settings with { }));
        Assert.Same(first, again);
        Assert.Equal(1, first.Revision);
    }

    [Fact]
    public void ASpeedChangeKeepsTheLayoutAndSeeds()
    {
        SceneBuilder builder = Builder();
        LightsScene first = builder.Build(Inputs(Settings()));
        LightsScene faster = builder.Build(Inputs(Settings(s => s with { Current = s.Current with { Flash = new FlashSettings { Interval = 2 } } })));
        Assert.NotSame(first, faster);
        Assert.Same(first.Layout, faster.Layout);
        Assert.Equal(first.Flash.PatternSeed, faster.Flash.PatternSeed);
        Assert.Equal(first.Revision + 1, faster.Revision);
    }

    [Fact]
    public void ANewPatternOrLayoutDrawsNewSeeds()
    {
        SceneBuilder builder = Builder();
        LightsScene first = builder.Build(Inputs(Settings()));
        LightsScene random = builder.Build(Inputs(Settings(s => s with { Current = s.Current with { Flash = new FlashSettings { Pattern = FlashPatternId.RandomFlashing } } })));
        Assert.NotEqual(first.Flash.PatternSeed, random.Flash.PatternSeed);

        // Random Flashing re-rolls when the lights are rebuilt (5.4).
        LightsScene rebuilt = builder.Build(Inputs(Settings(s => s with { Current = s.Current with { Flash = new FlashSettings { Pattern = FlashPatternId.RandomFlashing } } })), relayout: true);
        Assert.NotSame(random.Layout, rebuilt.Layout);
        Assert.NotEqual(random.Flash.ClassicRandomSeed, rebuilt.Flash.ClassicRandomSeed);
    }

    [Fact]
    public void ADisplayChangeLaysOutAgain()
    {
        SceneBuilder builder = Builder();
        LightsScene first = builder.Build(Inputs(Settings()));
        DisplayInfo[] moved = [FakeDisplayService.Display(1, 0, primary: true, taskbar: 96), FakeDisplayService.Display(2, -3840, primary: false)];
        LightsScene second = builder.Build(Inputs(Settings(), displays: moved));
        Assert.NotSame(first.Layout, second.Layout);
        Assert.Equal(2064, second.Layout.Displays[0].Target.Area.Height);
    }

    [Fact]
    public void LightsComingOnPowerUp()
    {
        SceneBuilder builder = Builder();
        LightsScene off = builder.Build(Inputs(Settings(s => s with { Lights = s.Lights with { On = false } })));
        LightsScene on = builder.Build(Inputs(Settings()));
        Assert.Equal(SceneTransition.ShortPowerUp, SceneRules.ChooseTransition(SettingsChange.Edit("Turn on the lights"), off, on));
    }

    [Fact]
    public void ALoadedThemeThatChangesTheLightsPlaysTheThemeTransition()
    {
        SceneBuilder builder = Builder();
        LightsScene before = builder.Build(Inputs(Settings()));
        LightsScene after = builder.Build(Inputs(Settings(s => s with { Current = s.Current with { Arrangement = StandardEverywhere() } })));
        Assert.Equal(SceneTransition.ThemeTransition, SceneRules.ChooseTransition(new SettingsChange(SettingsChangeKind.ThemeLoaded, "Load"), before, after));
        Assert.Equal(SceneTransition.ThemeTransition, SceneRules.ChooseTransition(new SettingsChange(SettingsChangeKind.AutomaticTheme, "Switch"), before, after));
        Assert.Equal(SceneTransition.Automatic, SceneRules.ChooseTransition(SettingsChange.Edit("Use Standard Bulbs everywhere"), before, after));
        Assert.Equal(SceneTransition.Automatic, SceneRules.ChooseTransition(null, before, after));
    }

    [Fact]
    public void AThemeThatOnlyChangesMusicDoesNotReplayTheLights()
    {
        SceneBuilder builder = Builder();
        LightsScene before = builder.Build(Inputs(Settings()));
        LightsScene same = builder.Build(Inputs(Settings(s => s with { Current = s.Current with { Music = new CurrentMusic { Mode = PlayMode.Never } } })));
        Assert.Same(before, same);
        Assert.Equal(SceneTransition.Automatic, SceneRules.ChooseTransition(new SettingsChange(SettingsChangeKind.ThemeLoaded, "Load"), before, same));
    }
}
