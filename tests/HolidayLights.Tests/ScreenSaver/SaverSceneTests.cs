using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class SaverSceneTests
{
    private const uint Black = 0xFF000000;

    [Fact]
    public void DefaultSaver_DrawsThePictureBulbsAndMessage_In54Places()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            DisplayInfo display = SaverTestKit.Display(1920, 1080);
            SaverScene scene = kit.MainScene(display, new AppSettings());
            CpuSaverRenderer renderer = kit.Services.CreateRenderer(scene, 1.0, new SaverSpriteCache());

            renderer.Render(0f);
            PremultipliedImage frame = renderer.Frame;

            // Santa Candle, centred at a third of the free height: (1920 - 282) / 2, (1080 - 297) / 3.
            SaverPicture picture = Assert.IsType<SaverPicture>(scene.Picture);
            Assert.Equal(RectI.FromXYWH(819, 261, 282, 297), picture.Layout.Rectangles[0]);
            Assert.Equal(Bgra32.Premultiply(picture.Image[141, 150]), frame[819 + 141, 261 + 150]);

            // "Happy Holidays!" in red below the picture (gaps of a tenth of the 522 DIPs under it).
            SaverMessage message = Assert.IsType<SaverMessage>(scene.Message);
            Assert.Equal((240, 1680, 610, 976), (message.Range.Left, message.Range.Right, message.Range.MinTop, message.Range.MaxBottom));
            Assert.True(CountPixels(frame, new RectI(240, 600, 1680, 610 + message.TextHeight), IsRed) > 500);

            // Christmas 1 around the whole display (the saver covers the taskbar); nothing yet in the empty sky.
            Assert.True(CountPixels(frame, new RectI(0, 0, 1920, 40), p => p != Black) > 2000);
            Assert.True(CountPixels(frame, new RectI(0, 1040, 1920, 1080), p => p != Black) > 2000);
            Assert.Equal(Black, frame[400, 200]);
        });
    }

    [Fact]
    public void HighDpiDisplay_SimulatesInDipsAndDrawsInPixels()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            DisplayInfo display = SaverTestKit.Display(3840, 2160, 144);
            SaverScene scene = kit.MainScene(display, new AppSettings());
            CpuSaverRenderer renderer = kit.Services.CreateRenderer(scene, 1.0, new SaverSpriteCache());

            renderer.Render(0f);

            Assert.Equal(new SaverField(2560, 1440), scene.Field);
            Assert.Equal(new SizeI(3840, 2160), renderer.Frame.Size);
            Assert.Equal(RectI.FromXYWH(1139, 381, 282, 297), scene.Picture!.Layout.Rectangles[0]);
            // The picture lands at (1709, 572) in pixels, 423 x 446 pixels big (MMPX, then area-averaged).
            Assert.NotEqual(Black, renderer.Frame[1709 + 211, 572 + 223]);
            Assert.Equal(Black, renderer.Frame[1709 - 2, 572 + 223]);
        });
    }

    [Fact]
    public void OtherDisplays_ShowBulbsAndTheAnimationButNoPictureOrMessage()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            SaverOptions options = SaverOptions.FromSettings(new AppSettings(), 1);
            DisplayInfo second = SaverTestKit.Display(1920, 1080, left: 1920, primary: false, number: 2);

            SaverScene scene = kit.Services.CreateScenes([new SaverDisplayPlan(second, true, false)], options, null, 1, 0)[0];

            Assert.Null(scene.Picture);
            Assert.Null(scene.Message);
            Assert.NotNull(scene.Bulbs);
            Assert.IsType<SnowModule>(scene.Simulation.Module);
            Assert.All(scene.Bulbs.Layout.Placements, p => Assert.True(second.Bounds.Contains(p.Bounds.TopLeft)));
        });
    }

    [Fact]
    public void MainDisplayOnly_LeavesTheOtherDisplaysBlack()
    {
        var settings = new AppSettings
        {
            Saver = new SaverDeviceSettings { ShowOn = SaverDisplays.MainOnly },
            Current = new ThemeableSettings { Saver = new SaverLook { Background = new RgbColor(0, 0, 128) } },
        };
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit(settings);
            SaverOptions options = SaverOptions.FromSettings(settings, 1);
            IReadOnlyList<SaverDisplayPlan> plans = SaverDisplayPlan.For(
                [SaverTestKit.Display(800, 600), SaverTestKit.Display(800, 600, left: 800, primary: false, number: 2)], options.ShowOn);

            IReadOnlyList<SaverScene> scenes = kit.Services.CreateScenes(plans, options, null, 1, 0);

            Assert.Equal(new RgbColor(0, 0, 128), scenes[0].Background);
            Assert.Equal(RgbColor.Black, scenes[1].Background);
            Assert.Null(scenes[1].Bulbs);
            Assert.Null(scenes[1].Simulation.Module);
        });
    }

    [Fact]
    public void Snow_CatchesOnOpaqueBulbPixelsOnly()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            DisplayInfo display = SaverTestKit.Display(1920, 1080);
            SaverScene scene = kit.MainScene(display, new AppSettings());
            SaverBulbs bulbs = scene.Bulbs!;
            BulbSnowCatcher catcher = BulbSnowCatcher.Create(bulbs.Layout, display, scene.Field, kit.Catalog);

            int opaque = 0;
            int caught = 0;
            foreach (BulbPlacement placement in bulbs.Layout.Placements.Take(10))
            {
                Assert.True(kit.Catalog.TryGetBulb(placement.BulbId, out IBulb? bulb));
                Rgba32Image art = bulb.GetCell(placement.Slot, placement.Flavor, 0).Image;
                for (int y = 0; y < art.Height; y++)
                {
                    for (int x = 0; x < art.Width; x++)
                    {
                        bool isOpaque = Bgra32.A(art[x, y]) == 255;
                        opaque += isOpaque ? 1 : 0;
                        bool hit = catcher.TryCatch(placement.Bounds.Left + x, placement.Bounds.Top + y, out uint color);
                        caught += hit ? 1 : 0;
                        if (isOpaque && hit)
                        {
                            uint expected = (art[x, y] & 0x00FFFFFF) == 0
                                ? (placement.Slot is CellSlot.Left or CellSlot.Right ? BulbSnowCatcher.GreyOnVertical : BulbSnowCatcher.GreyOnHorizontal)
                                : BulbSnowCatcher.White;
                            Assert.Equal(expected, color);
                        }
                    }
                }
            }

            Assert.True(opaque > 1000);
            Assert.InRange(caught, opaque * 9 / 10, opaque + opaque / 10);
            Assert.False(catcher.TryCatch(960, 540, out _));
            Assert.False(catcher.TryCatch(-1, 0, out _));
        });
    }

    [Fact]
    public void FallingSnow_PilesUpAlongTheBottom()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            SaverScene scene = kit.MainScene(SaverTestKit.Display(640, 480), TestKitSettings(SaverAnimations.Snow), seed: 5);
            CpuSaverRenderer renderer = kit.Services.CreateRenderer(scene, 1.0, new SaverSpriteCache());
            var run = new SaverRun([scene], 0);
            long step = System.Diagnostics.Stopwatch.Frequency * StepClock.TickMilliseconds / 1000;
            int stamps = 0;
            for (int i = 1; i <= 600; i++)
            {
                run.Advance(i * step, s =>
                {
                    stamps += s.Simulation.NewStamps.Count;
                    renderer.ApplyStep();
                });
            }

            renderer.Render(run.Progress);

            Assert.True(stamps > 50);
            Assert.True(CountPixels(renderer.Frame, new RectI(40, 460, 600, 478), p => p == 0xFFFFFFFF) > 30);
            Assert.Equal(600, scene.Simulation.StepCount);
        });
    }

    /// <summary>Only the animation: no bulbs, picture or message.</summary>
    private static AppSettings TestKitSettings(string animation) => new()
    {
        Current = new ThemeableSettings
        {
            Arrangement = SlotAssignment.Empty,
            Saver = new SaverLook { Animation = animation, Picture = SaverPictures.None, Message = "" },
        },
    };

    private static bool IsRed(uint pixel) => Bgra32.R(pixel) > 200 && Bgra32.G(pixel) < 60 && Bgra32.B(pixel) < 60;

    private static int CountPixels(PremultipliedImage image, RectI area, Func<uint, bool> match)
    {
        int count = 0;
        for (int y = Math.Max(0, area.Top); y < Math.Min(image.Height, area.Bottom); y++)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(image.Width, area.Right); x++)
            {
                count += match(image[x, y]) ? 1 : 0;
            }
        }

        return count;
    }
}
