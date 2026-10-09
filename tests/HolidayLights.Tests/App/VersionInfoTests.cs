using System.Diagnostics;
using System.Reflection;
using HolidayLights.App.Shell;
using HolidayLights.Tests.App.Fakes;

namespace HolidayLights.Tests.App;

/// <summary>"Copy Version Info" (PRODUCT-SPEC 3.9) and the free-running clock of sessions without lights (5.5).</summary>
public sealed class VersionInfoTests
{
    [Fact]
    public void TheProgramVersionIsSix() =>
        Assert.Matches(@"^6\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", VersionInfo.ProgramVersion);

    [Fact]
    public void TheProgramNamesItsCreatorCopyrightAndHomePage()
    {
        Assembly program = typeof(VersionInfo).Assembly;

        Assert.Equal("Holiday Lights", program.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal("StarrLord", program.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal(
            "Holiday Lights 6 (c) 2026 StarrLord. Based on Holiday Lights 5.4 (c) 1993-2003 Tiger Technologies. Bulb art and music copyright their respective authors.",
            program.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        Assert.Equal("StarrLord", ProjectInfo.Author);
        Assert.Equal("https://github.com/starrlord/holidaylights", ProjectInfo.HomePage);
        Assert.Equal("MIT License", ProjectInfo.License);
    }

    [Fact]
    public void TheReportNamesVersionBuildDisplaysLayersAndMidi()
    {
        var status = new LightsStatus
        {
            Health = LightsHealth.Running,
            Requested = LayerMode.BehindIcons,
            Displays = [new DisplayLayerStatus("display-1", LayerMode.BehindIcons, false), new DisplayLayerStatus("display-2", LayerMode.InFrontOfIcons, false)],
        };
        string text = VersionInfo.Format(new VersionInfoInputs("6.0.0", 26300, FakeDisplayService.ReferencePc(), LayerMode.BehindIcons, status, "", ["Microsoft GS Wavetable Synth"]));

        Assert.Contains("Holiday Lights - Modern Edition, version 6.0.0", text);
        Assert.Contains("Windows build 26300", text);
        Assert.Contains("Lights requested: behind the icons (Running)", text);
        Assert.Contains("Display 1 (main): 3840 x 2160 at 150 %, position 0, 0, work area 3840 x 2088 at 0, 0; lights behind the icons", text);
        Assert.Contains("Display 2: 3840 x 2160 at 150 %, position -3840, 0, work area 3840 x 2088 at -3840, 0; lights in front of the icons", text);
        Assert.Contains("MIDI output: Automatic (devices: Microsoft GS Wavetable Synth)", text);
    }

    [Fact]
    public void RestingAndMissingLayersAreNamed()
    {
        var status = new LightsStatus
        {
            Health = LightsHealth.Running,
            Displays = [new DisplayLayerStatus("display-1", null, true)],
        };
        string text = VersionInfo.Format(new VersionInfoInputs("6.0.0", 26300, FakeDisplayService.ReferencePc(), LayerMode.OnTop, status, "Synth", []));
        Assert.Contains("lights resting", text);
        Assert.EndsWith("at -3840, 0; no lights", text.Split(Environment.NewLine)[4]);
        Assert.Contains("MIDI output: Synth (devices: none)", text);
    }

    [Fact]
    public void TheFreeClockRestartsOnANewPatternAndChangesSpeedAtTheNextStep()
    {
        LightsScene scene = LightsScene.Empty with { LightsOn = true, Interval = 5, Flash = new FlashOptions { Pattern = FlashPatternId.FlashTogether } };
        var clock = new FreeRunningClock(scene);
        int changes = 0;
        clock.ClockChanged += (_, _) => changes++;
        StepClock first = clock.Clock;
        Assert.Equal(5, first.Interval);

        clock.Follow(scene);
        Assert.Same(first, clock.Clock);

        clock.Follow(scene with { Interval = 2 });
        Assert.Equal(2, clock.Clock.Interval);
        Assert.NotNull(clock.Clock.Previous);

        long before = Stopwatch.GetTimestamp();
        clock.Follow(scene with { Interval = 2, Flash = new FlashOptions { Pattern = FlashPatternId.Twinkle } });
        Assert.Equal(0, clock.Clock.EpochStep);
        Assert.True(clock.Clock.EpochTimestamp >= before);
        Assert.Null(clock.Clock.Previous);
        Assert.Equal(2, changes);
    }
}
