using HolidayLights.Audio.Events;
using HolidayLights.Rendering;
using HolidayLights.Rendering.Overlays;
using HolidayLights.Rendering.Shell;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live check of the on-screen pill (PRODUCT-SPEC 3.12) and Identify (3.7) on the real desktop (runs only with
/// <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>): the pill appears on the display under the mouse pointer, top centre, 24 DIP below the
/// work-area top, and each display shows its number at the bottom left of its work area; the captures must equal the
/// overlay pictures composed over what was there before (premultiplied source-over). The overlays close themselves.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LiveOverlayProbe : IClassFixture<RealLights>
{
    private readonly RealLights lights;
    private readonly ITestOutputHelper output;

    public LiveOverlayProbe(RealLights lights, ITestOutputHelper output)
    {
        this.lights = lights;
        this.output = output;
    }

    [LiveFact]
    public async Task ShowsThePillAndIdentifyWhereTheSpecSays()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        RectI screen = displays.Select(d => d.Bounds).Aggregate((a, b) => a.Union(b));
        string folder = LiveSupport.OutputFolder("overlays");
        MonitorState monitor = MonitorTopology.UnderCursor() ?? throw new InvalidOperationException("No monitor under the pointer.");
        var log = new RecordingLog();
        using LightsPresenter presenter = LiveSupport.CreatePresenter(lights, new MusicEventHub(), log);
        try
        {
            presenter.Start();
            // The lights hold still under the overlays, so the desktop behind them is the same before and after.
            presenter.Apply(lights.Scene(lights.Theme("Christmas 1"), displays, LayerMode.BehindIcons, r => r with { Pattern = FlashPatternId.DontFlash }), SceneTransition.None);
            Assert.True(LiveSupport.WaitFor(() => LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons), TimeSpan.FromSeconds(10)));
            Thread.Sleep(500);

            // The pill with its second line (the first uses of the location hot key).
            var pill = new PillRequest(PillGlyph.OnTop, "Bulbs on top of all windows", "Press Ctrl+Alt+Shift+B again to put them back.", TimeSpan.FromSeconds(4));
            PremultipliedImage pillArt = OverlayArt.Pill(pill, showSecondLine: true, monitor.Dpi);
            var pillBounds = RectI.FromXYWH(
                monitor.WorkArea.Left + ((monitor.WorkArea.Width - pillArt.Width) / 2),
                monitor.WorkArea.Top + (int)Math.Round(24 * monitor.Dpi / 96.0),
                pillArt.Width,
                pillArt.Height);
            ScreenShot before = ScreenShot.Take(pillBounds);
            presenter.ShowPill(pill);
            Thread.Sleep(700);
            ScreenShot shown = ScreenShot.Take(pillBounds);
            DesktopCapture.SavePng(Path.Combine(folder, "pill.png"), shown.Pixels, pillBounds.Width, pillBounds.Height);
            double pillMatch = Composed(pillArt, before, shown);
            output.WriteLine($"Pill at {pillBounds} on {monitor.DeviceName} ({monitor.Dpi} DPI): {pillMatch:P2} of its pixels equal the picture over the desktop.");
            Assert.True(pillMatch >= 0.99, $"The pill matches only {pillMatch:P2}.");

            // Identify: every display's number for 3 s.
            var cards = displays.Select(d =>
            {
                PremultipliedImage art = OverlayArt.Identify(d.Number, d.Dpi);
                int margin = (int)Math.Round(48 * d.Scale);
                var bounds = RectI.FromXYWH(d.WorkArea.Left + margin, d.WorkArea.Bottom - margin - art.Height, art.Width, art.Height);
                return (Display: d, Art: art, Bounds: bounds, Before: ScreenShot.Take(bounds));
            }).ToList();
            presenter.ShowIdentify(displays, TimeSpan.FromSeconds(3));
            Thread.Sleep(700);
            foreach ((DisplayInfo display, PremultipliedImage art, RectI bounds, ScreenShot cardBefore) in cards)
            {
                ScreenShot card = ScreenShot.Take(bounds);
                DesktopCapture.SavePng(Path.Combine(folder, $"identify-display{display.Number}.png"), card.Pixels, bounds.Width, bounds.Height);
                double match = Composed(art, cardBefore, card);
                output.WriteLine($"Identify {display.Number} at {bounds}: {match:P2} of its pixels equal the picture over the desktop.");
                Assert.True(match >= 0.99, $"Identify {display.Number} matches only {match:P2}.");
            }

            // Both close themselves: the pill after 150 + 4000 + 150 ms, Identify after 3.3 s.
            Thread.Sleep(4_200);
            Assert.True(LiveSupport.WaitFor(() => LiveSupport.CountLayerWindows() == displays.Count, TimeSpan.FromSeconds(2)), "An overlay did not close itself.");
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: false).WaitAsync(TimeSpan.FromSeconds(5));
        }

        LiveSupport.WriteLog(log, output);
        Assert.Equal(0, LiveSupport.CountLayerWindows());
        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
    }

    /// <summary>The share of pixels where the capture equals the premultiplied picture composed over the earlier capture.</summary>
    private static double Composed(PremultipliedImage art, ScreenShot before, ScreenShot after)
    {
        int matching = 0;
        for (int i = 0; i < art.Pixels.Length; i++)
        {
            uint source = art.Pixels[i];
            uint destination = before.Pixels[i];
            double inverse = 1 - ((source >> 24) / 255.0);
            static uint Over(uint s, uint d, int shift, double inverse) => (uint)Math.Min(255, Math.Round(((s >> shift) & 0xFF) + (((d >> shift) & 0xFF) * inverse)));
            uint expected = 0xFF000000 | Over(source, destination, 16, inverse) << 16 | Over(source, destination, 8, inverse) << 8 | Over(source, destination, 0, inverse);
            matching += ScreenExpectations.Distance(expected, after.Pixels[i]) <= 3 ? 1 : 0;
        }

        return (double)matching / art.Pixels.Length;
    }
}
