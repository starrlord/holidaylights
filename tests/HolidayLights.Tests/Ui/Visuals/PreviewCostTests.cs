using System.Windows;
using System.Windows.Controls;
using HolidayLights.App.Preview;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.Visuals;

/// <summary>
/// What the bulb previews cost (PRODUCT-SPEC 5.14): tiles are prepared off the UI thread and show a run of flavors cropped
/// to the well; repeated frames are not composited again; nothing draws while the window is minimized or the lights rest.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class PreviewCostTests(UiThread ui)
{
    private static readonly string StandardBulbs = BulbIds.BuiltIn("standard-bulbs");

    private static readonly FlashOptions FlashTogether = new() { Pattern = FlashPatternId.FlashTogether, SmoothFading = false };

    [Fact]
    public void ATileShowsARunOfFlavorsCroppedToTheWell() => ui.Run(() =>
    {
        using UiTestServices services = Ready();
        Assert.True(services.Bulbs.TryGetBulb(StandardBulbs, out IBulb? standard));
        Assert.True(standard.GetFlavorCount(Side.Top) > 1);

        // A Bulb List tile: a 96 DIP well at 150 %, art at most twice its size.
        var tile = new LightStrip { SingleBulbId = StandardBulbs, Side = Side.Top, BulbHeight = 56, MaxArtScale = 2, FallbackToListPreview = true, CropRun = true };
        StripModel model = StripModel.Create(services, tile, 1.5, new Size(96, 56));
        BulbPlacement[] run = [.. model.Layout.Placements.OrderBy(p => p.Bounds.Left)];
        Assert.True(run.Length >= 3, $"{run.Length} bulbs in the run.");
        Assert.True(run[0].Bounds.Left < 12, "The run starts at the left edge of the well.");
        Assert.True(run[1].Bounds.Left < model.PixelSize.Width, "The second flavor shows (cropped) in the well.");
        Assert.True(run[^1].Bounds.Right > model.PixelSize.Width, "The run continues past the well and is cropped.");
        Assert.NotEqual(run[0].Flavor, run[1].Flavor);
    });

    [Fact]
    public void ATileIsPreparedOffTheUiThreadAndThenShown() => ui.Run(() =>
    {
        using UiTestServices services = Ready();
        var tile = new LightStrip { Services = services, SingleBulbId = BulbIds.AddOn("Arrow"), Side = Side.Top, BulbHeight = 56, MaxArtScale = 2, Glow = StripGlow.On, CropRun = true, Width = 96, Height = 56 };
        bool preparingWhenLoaded = false;
        LightsLayout? layoutWhenLoaded = null;
        tile.Loaded += (_, _) =>
        {
            preparingWhenLoaded = tile.IsPreparing;
            layoutWhenLoaded = tile.CurrentLayout;
        };
        Host(tile, window =>
        {
            Assert.True(preparingWhenLoaded, "The run is prepared on the thread pool.");
            Assert.Null(layoutWhenLoaded);
            for (int i = 0; i < 50 && tile.IsPreparing; i++)
            {
                UiHarness.Pump(TimeSpan.FromMilliseconds(40));
            }

            Assert.False(tile.IsPreparing);
            Assert.True(tile.HasBulbs);
            Assert.NotEmpty(Assert.IsType<LightsLayout>(tile.CurrentLayout).Placements);
            Assert.True(tile.CompositedFrames > 0);
        });
    });

    [Fact]
    public void RepeatedAndUnchangedFramesAreNotCompositedAgain() => ui.Run(() =>
    {
        using UiTestServices services = Ready();
        var strip = new LightStrip
        {
            Services = services,
            Arrangement = SlotAssignment.Empty.WithEdge(Side.Top, [StandardBulbs]),
            Side = Side.Top,
            BulbHeight = 20,
            Glow = StripGlow.On,
            Flash = FlashTogether,
            Width = 600,
            Height = 24,
        };
        Host(strip, window =>
        {
            var animated = (IStepAnimated)strip;
            StepClock clock = services.Lights.Clock.Clock;
            animated.ShowStep(100, clock);
            animated.ShowStep(101, clock);
            int afterTwo = strip.CompositedFrames;
            animated.ShowStep(101, clock);
            animated.ShowStep(102, clock);
            animated.ShowStep(103, clock);
            animated.ShowStep(104, clock);
            Assert.Equal(afterTwo, strip.CompositedFrames);
        });
    });

    [Fact]
    public void StripsAndStagesStopWhileTheLightsRestAndGoOnAfterwards() => ui.Run(() =>
    {
        using UiTestServices services = Ready();
        var strip = new LightStrip { Services = services, Side = Side.Top, BulbHeight = 20, SmoothAnimation = true, Flash = FlashTogether, Width = 600, Height = 24 };
        var stage = new LightStage { Services = services, Flash = FlashTogether, Width = 400, Height = 230 };
        PreviewAnimation.SetIsHot(strip, true);
        var panel = new StackPanel { Children = { strip, stage } };
        Host(panel, window =>
        {
            Assert.True(strip.IsAnimating);
            Assert.True(stage.IsAnimating);

            foreach (PauseReasons reason in new[] { PauseReasons.DisplayOff, PauseReasons.SessionLocked, PauseReasons.UserNotPresent })
            {
                services.LightsFake.SetStatus(services.Lights.Status with { Paused = reason });
                Assert.False(strip.IsAnimating, $"The strip draws while {reason}.");
                Assert.False(stage.IsAnimating, $"The stage draws while {reason}.");
                int frames = strip.CompositedFrames + stage.CompositedFrames;
                UiHarness.Pump(TimeSpan.FromMilliseconds(400));
                Assert.Equal(frames, strip.CompositedFrames + stage.CompositedFrames);

                services.LightsFake.SetStatus(services.Lights.Status with { Paused = PauseReasons.None });
                Assert.True(strip.IsAnimating);
                Assert.True(stage.IsAnimating);
            }

            // A full-screen app on one display does not stop the Settings previews.
            services.LightsFake.SetStatus(services.Lights.Status with { Paused = PauseReasons.Presentation });
            Assert.True(strip.IsAnimating);
            services.LightsFake.SetStatus(services.Lights.Status with { Paused = PauseReasons.None });
        });
    });

    [Fact]
    public void TheStringOfLightsStopsWhileItsWindowIsMinimized() => ui.Run(() =>
    {
        using UiTestServices services = Ready();
        var strip = new LightStrip { Services = services, Side = Side.Top, BulbHeight = 20, SmoothAnimation = true, Flash = FlashTogether, Width = 600, Height = 24 };
        PreviewAnimation.SetIsHot(strip, true);
        Host(strip, window =>
        {
            Assert.True(strip.IsAnimating);
            window.WindowState = WindowState.Minimized;
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            Assert.False(strip.IsAnimating);
            int frames = strip.CompositedFrames;
            UiHarness.Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal(frames, strip.CompositedFrames);

            window.WindowState = WindowState.Normal;
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            Assert.True(strip.IsAnimating);
        });
    });

    private static UiTestServices Ready()
    {
        var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        return services;
    }

    private static void Host(FrameworkElement element, Action<Window> test)
    {
        var window = new Window { Content = element };
        UiHarness.ApplyAppResources(window, dark: true);
        UiHarness.PlaceOffScreen(window, 800, 400);
        window.Show();
        try
        {
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            test(window);
        }
        finally
        {
            window.Close();
        }
    }
}
