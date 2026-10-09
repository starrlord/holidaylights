using System.Diagnostics;
using HolidayLights.App.ScreenSaver.Preview;
using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.Core.Flash;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>
/// The Screen Saver page preview (PO decision 8): the enlarged top-left corner shows the bulbs at a legible size, drawn by a
/// renderer that covers only that part of the display and matches the whole-display drawing pixel for pixel.
/// </summary>
public sealed class SaverPreviewInsetTests
{
    [Fact]
    public void InsetZoom_ShowsOneDisplayDipPerPixel_WhenThatMagnifiesTheMiniature()
    {
        // The reference main display in the page's 480-pixel preview: the miniature is 1/8, the corner 1/1.5 (5.3 times larger).
        DisplayInfo reference = SaverTestKit.Display(3840, 2160, 144);
        Assert.Equal(1 / 1.5, SaverPreviewElement.InsetZoom(reference, 480.0 / 3840)!.Value, 6);

        // A 1080p display at 100 %: the corner is shown pixel for pixel.
        Assert.Equal(1.0, SaverPreviewElement.InsetZoom(SaverTestKit.Display(1920, 1080), 320.0 / 1920)!.Value, 6);

        // A miniature that is already at least half the corner's size needs no inset.
        Assert.Null(SaverPreviewElement.InsetZoom(SaverTestKit.Display(1280, 720), 0.6));
    }

    [Fact]
    public void InsetCorner_ShowsTheBulbsMuchLargerThanTheMiniature()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            DisplayInfo reference = SaverTestKit.Display(3840, 2160, 144);
            SaverScene scene = kit.MainScene(reference, new AppSettings());
            const double MiniatureZoom = 480.0 / 3840;
            double insetZoom = SaverPreviewElement.InsetZoom(reference, MiniatureZoom)!.Value;
            var sprites = new SaverSpriteCache();
            CpuSaverRenderer miniature = kit.Services.CreateRenderer(scene, MiniatureZoom, sprites);
            CpuSaverRenderer corner = kit.Services.CreateRenderer(scene, insetZoom, sprites, new RectI(0, 0, 192, 108));

            miniature.Render(0f);
            corner.Render(0f);

            Assert.Equal(new SizeI(192, 108), corner.Frame.Size);
            // The top band of bulbs: a few pixels tall in the miniature, tens of pixels in the corner.
            int miniatureBand = LitRows(miniature.Frame, 20, 100, 20);
            int cornerBand = LitRows(corner.Frame, 60, 190, 108);
            Assert.InRange(miniatureBand, 1, 8);
            Assert.True(cornerBand >= miniatureBand * 4, $"The corner shows the bulbs {cornerBand} pixels tall, the miniature {miniatureBand}.");
        });
    }

    [Theory]
    [InlineData(0, 0, 192, 108)]
    [InlineData(300, 120, 260, 150)]
    [InlineData(150, 400, 400, 140)]
    [InlineData(900, 500, 200, 200)]
    public void Viewport_DrawsExactlyThatPartOfTheWholeDisplay(int left, int top, int width, int height)
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            SaverScene scene = kit.MainScene(SaverTestKit.Display(1920, 1080), new AppSettings(), seed: 3);
            var sprites = new SaverSpriteCache();
            CpuSaverRenderer whole = kit.Services.CreateRenderer(scene, 0.5, sprites);
            var viewport = RectI.FromXYWH(left, top, width, height);
            CpuSaverRenderer part = kit.Services.CreateRenderer(scene, 0.5, sprites, viewport);
            var run = new SaverRun([scene], 0);
            long step = Stopwatch.Frequency * StepClock.TickMilliseconds / 1000;
            for (int i = 1; i <= 700; i++)
            {
                run.Advance(i * step + step / 3, _ =>
                {
                    whole.ApplyStep();
                    part.ApplyStep();
                });
            }

            whole.Render(run.Progress);
            part.Render(run.Progress);

            RectI expected = viewport.Intersect(new RectI(0, 0, whole.Frame.Width, whole.Frame.Height));
            Assert.Equal(expected, part.Viewport);
            Assert.Equal(expected.Size, part.Frame.Size);
            for (int y = 0; y < part.Frame.Height; y++)
            {
                for (int x = 0; x < part.Frame.Width; x++)
                {
                    if (whole.Frame[expected.Left + x, expected.Top + y] != part.Frame[x, y])
                    {
                        Assert.Fail($"Pixel {x}, {y} of the viewport differs from the whole display.");
                    }
                }
            }
        });
    }

    [Fact]
    public void Viewport_OutsideTheDisplay_IsRejected()
    {
        StaThread.Run(() =>
        {
            using var kit = new SaverTestKit();
            SaverScene scene = kit.MainScene(SaverTestKit.Display(800, 600), new AppSettings());

            Assert.Throws<ArgumentOutOfRangeException>(() => kit.Services.CreateRenderer(scene, 0.5, new SaverSpriteCache(), new RectI(400, 0, 500, 100)));
        });
    }

    /// <summary>How many of the top rows have a bright pixel (a lit bulb) between two columns.</summary>
    private static int LitRows(PremultipliedImage frame, int fromX, int toX, int maxRows)
    {
        int rows = 0;
        for (int y = 0; y < Math.Min(maxRows, frame.Height); y++)
        {
            bool lit = false;
            for (int x = fromX; x < Math.Min(toX, frame.Width) && !lit; x++)
            {
                uint pixel = frame[x, y];
                lit = Math.Max(Bgra32.R(pixel), Math.Max(Bgra32.G(pixel), Bgra32.B(pixel))) > 120;
            }

            rows += lit ? 1 : 0;
        }

        return rows;
    }
}
