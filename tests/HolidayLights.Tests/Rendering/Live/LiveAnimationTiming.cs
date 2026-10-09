using System.Diagnostics;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Layers;
using HolidayLights.Tests.Shared;
using Vortice.DirectComposition;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live check of the DirectComposition animation contract the lights rely on (runs only with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>
/// and the displays on): a white square on black fades in linearly over 2 s from a begin time 500 ms in the past (as a fade
/// anchored at a step boundary is); captures at known times must show the matching brightness, which proves the cubic segment
/// convention and the re-anchoring of <see cref="OpacityCurve.StartingAt"/>.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LiveAnimationTiming
{
    private const int Size = 120;

    private readonly ITestOutputHelper output;

    public LiveAnimationTiming(ITestOutputHelper output) => this.output = output;

    [LiveDisplayFact]
    public void FadesFollowTheStopwatch()
    {
        var results = new List<(double Seconds, double Brightness)>();
        StaThread.Run(() =>
        {
            DesktopCapture.UsePhysicalPixels();
            using CompositionDevice device = CompositionDevice.Create(software: false);
            var bounds = RectI.FromXYWH(200, 300, Size, Size);
            using LayerWindow window = LayerWindow.TryCreateOverlay(bounds, device) ?? throw new InvalidOperationException("No overlay window.");

            // An opaque black square with a white centre: the centre's brightness is the white visual's opacity.
            var black = new PremultipliedImage(Size, Size, Enumerable.Repeat(0xFF000000u, Size * Size).ToArray());
            var white = new PremultipliedImage(Size, Size, Enumerable.Repeat(0xFFFFFFFFu, Size * Size).ToArray());
            using IDCompositionSurface blackSurface = device.CreateSurface(black)!;
            using IDCompositionSurface whiteSurface = device.CreateSurface(white)!;
            using IDCompositionVisual root = device.CreateVisual();
            using IDCompositionVisual fading = device.CreateVisual();
            root.SetContent(blackSurface);
            fading.SetContent(whiteSurface);
            root.AddVisual(fading, true, null);
            using IDCompositionEffectGroup effect = device.CreateEffectGroup();
            fading.SetEffect(effect);

            long begin = Stopwatch.GetTimestamp() - Stopwatch.Frequency / 2;
            using IDCompositionAnimation animation = device.CreateAnimation(OpacityCurves.Ramp(begin, 0, 1, 2_000));
            effect.SetOpacity(animation);
            window.SetRoot(root);
            window.Show(WindowPositions.TopMost);
            Assert.True(device.TryCommit());

            foreach (double at in new[] { 0.8, 1.2, 1.6, 2.4 })
            {
                while ((Stopwatch.GetTimestamp() - begin) / (double)Stopwatch.Frequency < at)
                {
                    Pump();
                    Thread.Sleep(2);
                }

                uint[] pixels = DesktopCapture.Capture(RectI.FromXYWH(bounds.Left + 40, bounds.Top + 40, 40, 40));
                results.Add((at, pixels.Average(p => (p >> 8) & 0xFF) / 255.0));
            }

            window.Hide();
        });

        foreach ((double seconds, double brightness) in results)
        {
            output.WriteLine($"t = {seconds:F1} s: brightness {brightness:F3} (expected {Math.Min(1, seconds / 2):F2})");
        }

        foreach ((double seconds, double brightness) in results)
        {
            Assert.InRange(brightness, Math.Min(1, seconds / 2) - 0.08, Math.Min(1, seconds / 2) + 0.08);
        }
    }

    private static void Pump()
    {
        while (User32.PeekMessageW(out MSG message, 0, 0, 0, Win32Constants.PmRemove))
        {
            User32.TranslateMessage(in message);
            User32.DispatchMessageW(in message);
        }
    }
}
