using System.Diagnostics;
using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Layers;
using HolidayLights.Rendering.Presentation;
using HolidayLights.Rendering.Shell;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Overlays;

/// <summary>
/// The on-screen pill (hot-key feedback, PRODUCT-SPEC 3.12) and the Identify numbers (3.7): topmost, click-through,
/// non-activating windows with one DirectComposition picture each, faded in and out with opacity animations. Lights thread only.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private const double PillTopMargin = 24;
    private const double IdentifyMargin = 48;

    private readonly CompositionDevice device;
    private readonly List<Overlay> identify = [];
    private Overlay? pill;

    /// <summary>Creates the controller.</summary>
    public OverlayController(CompositionDevice device) => this.device = device;

    /// <summary>Overlay windows currently shown.</summary>
    public int Count => identify.Count + (pill is null ? 0 : 1);

    /// <summary>Shows the pill at the top centre of a display's work area (replacing a pill that is still shown).</summary>
    public void ShowPill(PillRequest request, MonitorState monitor, bool reducedMotion, long now, AnimationBatch batch)
    {
        bool replacing = pill is not null;
        pill?.Dispose();
        pill = null;

        bool secondLine = !string.IsNullOrEmpty(request.SecondLine) && request.SecondLineDuration > TimeSpan.Zero;
        PremultipliedImage image = OverlayArt.Pill(request, secondLine, monitor.Dpi);
        double scale = monitor.Dpi / 96.0;
        int x = monitor.WorkArea.Left + (monitor.WorkArea.Width - image.Width) / 2;
        int y = monitor.WorkArea.Top + (int)Math.Round(PillTopMargin * scale);
        double hold = secondLine ? Math.Max(MotionTimings.PillHold, request.SecondLineDuration.TotalMilliseconds) : MotionTimings.PillHold;
        double fade = reducedMotion ? 0 : MotionTimings.OverlayFade;
        pill = Overlay.TryCreate(device, image, RectI.FromXYWH(x, y, image.Width, image.Height), now, replacing ? 0 : fade, hold, fade, batch);
    }

    /// <summary>Shows each display's number at the bottom-left of its work area for <paramref name="duration"/>.</summary>
    public void ShowIdentify(IReadOnlyList<DisplayInfo> displays, TimeSpan duration, bool reducedMotion, long now, AnimationBatch batch)
    {
        foreach (Overlay overlay in identify)
        {
            overlay.Dispose();
        }

        identify.Clear();
        double fade = reducedMotion ? 0 : MotionTimings.OverlayFade;
        foreach (DisplayInfo display in displays)
        {
            PremultipliedImage image = OverlayArt.Identify(display.Number, display.Dpi);
            int margin = (int)Math.Round(IdentifyMargin * display.Scale);
            var bounds = RectI.FromXYWH(display.WorkArea.Left + margin, display.WorkArea.Bottom - margin - image.Height, image.Width, image.Height);
            if (Overlay.TryCreate(device, image, bounds, now, fade, duration.TotalMilliseconds, fade, batch) is { } overlay)
            {
                identify.Add(overlay);
            }
        }
    }

    /// <summary>Starts fade-outs and destroys finished overlays.</summary>
    /// <param name="now">The current timestamp.</param>
    /// <param name="batch">The animation batch.</param>
    /// <param name="changed">Set when something changed that needs a commit.</param>
    /// <returns>The next time an overlay needs attention, or null.</returns>
    public long? Tick(long now, AnimationBatch batch, ref bool changed)
    {
        long? next = null;
        if (pill is not null && !pill.Tick(now, batch, ref next, ref changed))
        {
            pill.Dispose();
            pill = null;
            changed = true;
        }

        for (int i = identify.Count - 1; i >= 0; i--)
        {
            if (!identify[i].Tick(now, batch, ref next, ref changed))
            {
                identify[i].Dispose();
                identify.RemoveAt(i);
                changed = true;
            }
        }

        return next;
    }

    /// <summary>Destroys every overlay.</summary>
    public void Dispose()
    {
        pill?.Dispose();
        pill = null;
        foreach (Overlay overlay in identify)
        {
            overlay.Dispose();
        }

        identify.Clear();
    }

    /// <summary>One overlay window: fade in, hold, fade out, destroy.</summary>
    private sealed class Overlay : IDisposable
    {
        private readonly LayerWindow window;
        private readonly IDCompositionVisual visual;
        private readonly IDCompositionSurface surface;
        private readonly GroupOpacity opacity;
        private readonly long fadeOutAt;
        private readonly long destroyAt;
        private readonly double fadeOut;
        private bool fadingOut;

        private Overlay(LayerWindow window, IDCompositionVisual visual, IDCompositionSurface surface, GroupOpacity opacity, long fadeOutAt, long destroyAt, double fadeOut)
        {
            this.window = window;
            this.visual = visual;
            this.surface = surface;
            this.opacity = opacity;
            this.fadeOutAt = fadeOutAt;
            this.destroyAt = destroyAt;
            this.fadeOut = fadeOut;
        }

        public static Overlay? TryCreate(
            CompositionDevice device, PremultipliedImage image, RectI bounds, long now, double fadeIn, double hold, double fadeOut, AnimationBatch batch)
        {
            IDCompositionSurface? surface = device.CreateSurface(image);
            if (surface is null)
            {
                return null;
            }

            LayerWindow? window = LayerWindow.TryCreateOverlay(bounds, device);
            if (window is null)
            {
                surface.Dispose();
                return null;
            }

            IDCompositionVisual visual = device.CreateVisual();
            visual.SetContent(surface);
            var opacity = new GroupOpacity(device, visual);
            opacity.Set(OpacityCurves.Ramp(now, 0, 1, fadeIn), batch);
            window.SetRoot(visual);
            window.Show(WindowPositions.TopMost);
            long fadeOutAt = now + Ticks(fadeIn + hold);
            return new Overlay(window, visual, surface, opacity, fadeOutAt, fadeOutAt + Ticks(fadeOut), fadeOut);
        }

        /// <summary>Advances the overlay; false when it should be destroyed.</summary>
        public bool Tick(long now, AnimationBatch batch, ref long? next, ref bool changed)
        {
            if (now >= destroyAt)
            {
                return false;
            }

            changed |= opacity.SettleIfDue(now, batch);
            if (!fadingOut && now >= fadeOutAt)
            {
                fadingOut = true;
                opacity.Set(OpacityCurves.Ramp(now, opacity.Curve.Evaluate(now), 0, fadeOut), batch);
                changed = true;
            }

            long due = fadingOut ? destroyAt : fadeOutAt;
            next = next is { } pending ? Math.Min(pending, due) : due;
            return true;
        }

        public void Dispose()
        {
            window.Dispose();
            opacity.Dispose();
            visual.Dispose();
            surface.Dispose();
        }

        private static long Ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000);
    }
}
