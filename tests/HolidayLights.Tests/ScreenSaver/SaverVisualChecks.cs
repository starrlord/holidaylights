using System.Diagnostics;
using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>Runs only when <c>HOLIDAYLIGHTS_SAVER_FRAMES</c> names a folder: frame dumps to look at.</summary>
public sealed class SaverFramesFactAttribute : FactAttribute
{
    public const string Variable = "HOLIDAYLIGHTS_SAVER_FRAMES";

    public SaverFramesFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Writes PNG frames: set {Variable} to a folder to run it.";
        }
    }

    /// <summary>The folder (created when missing), or null when the variable is not set.</summary>
    public static string? Folder =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } folder ? Directory.CreateDirectory(folder).FullName : null;
}

/// <summary>Frame dumps of the CPU renderer for looking at: every animation, the looks and the placements.</summary>
public sealed class SaverVisualChecks(ITestOutputHelper output)
{
    [SaverFramesFact]
    public void DumpEveryAnimation()
    {
        StaThread.Run(() =>
        {
            foreach (string animation in SaverAnimations.All)
            {
                var look = new SaverLook { Animation = animation, Style = SaverAnimations.DefaultStyleFor(animation) };
                Dump($"animation-{animation.Replace("'", "", StringComparison.Ordinal)}", SaverTestKit.WithSaver(look), SaverTestKit.Display(1920, 1080), 1.0, 400);
            }
        }, TimeSpan.FromMinutes(5));
    }

    [SaverFramesFact]
    public void DumpLooksAndPlacements()
    {
        StaThread.Run(() =>
        {
            DisplayInfo reference = SaverTestKit.Display(3840, 2160, 144);
            Dump("reference-default", new AppSettings(), reference, 1.0, 900);
            Dump("reference-classic2003", new AppSettings { Look = LookPresets.ValuesOf(LookPreset.Classic2003)! }, reference, 1.0, 900);
            Dump("tile-snowman", SaverTestKit.WithSaver(new SaverLook { Picture = "bundled:Snowman.BMP", Placement = PicturePlacement.Tile, Animation = SaverAnimations.Balloons }),
                SaverTestKit.Display(1920, 1080), 1.0, 100);
            Dump("stretch-pumpkin", SaverTestKit.WithSaver(new SaverLook { Picture = "bundled:Pumpkin.BMP", Placement = PicturePlacement.Stretch, Animation = "Halloween", Style = SaverMovementStyle.Attraction }),
                SaverTestKit.Display(1920, 1080), 1.0, 100);
            Dump("gravity-eggs", SaverTestKit.WithSaver(new SaverLook { Picture = "bundled:Easter Bunny.BMP", Animation = "Easter Eggs", Style = SaverMovementStyle.GravityWell, Message = "Happy Easter!", Color = new RgbColor(255, 255, 0) }),
                SaverTestKit.Display(1920, 1080), 1.0, 1500);
            Dump("leaves-pile", SaverTestKit.WithSaver(new SaverLook { Picture = "bundled:Turkey.BMP", Animation = "Leaves", Style = SaverMovementStyle.FallingLeaves, Message = "Happy Thanksgiving!", Color = new RgbColor(128, 128, 0) }),
                SaverTestKit.Display(1920, 1080), 1.0, 2400);
            Dump("preview-small", new AppSettings(), reference, 0.06, 200);
        }, TimeSpan.FromMinutes(5));
    }

    private void Dump(string name, AppSettings settings, DisplayInfo display, double zoom, int steps)
    {
        using var kit = new SaverTestKit(settings);
        SaverScene scene = kit.MainScene(display, settings, seed: 2003);
        CpuSaverRenderer renderer = kit.Services.CreateRenderer(scene, zoom, new SaverSpriteCache());
        var run = new SaverRun([scene], 0);
        long step = Stopwatch.Frequency * StepClock.TickMilliseconds / 1000;
        var watch = Stopwatch.StartNew();
        for (int i = 1; i <= steps; i++)
        {
            run.Advance(i * step, _ => renderer.ApplyStep());
        }

        // Halfway into the next bulb step, so fades have finished and lit bulbs glow.
        run.Advance(steps * step + step * 5 / 2, _ => renderer.ApplyStep());
        renderer.Render(run.Progress);
        output.WriteLine($"{name}: {steps} steps and a frame in {watch.ElapsedMilliseconds} ms.");
        PremultipliedImage frame = renderer.Frame;
        SaverTestScreen.Save(Path.Combine(SaverFramesFactAttribute.Folder!, name + ".png"), frame.Pixels, frame.Width, frame.Height);
    }
}
