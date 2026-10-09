using HolidayLights.Audio.Events;
using HolidayLights.Rendering;
using HolidayLights.Rendering.Interop;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live check of the real lights on the real desktop (runs only with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>): the default theme of
/// the season (Halloween on the reference PC in October), the 5.4 default look (Christmas 1) and two new themes made of
/// bundled add-on bulbs, each in the three layer modes on every display, drawn by the presenter from the real catalog,
/// layout, flash engine and sprite pipeline. Every capture (BitBlt <c>SRCCOPY | CAPTUREBLT</c>, saved as PNGs) is compared
/// with what the Core alone says: each bulb's sprite at its layout position, pixel for pixel, and the additive glow
/// around the lit bulbs. The layers must never take a click or the focus. Then each theme plays its own pattern after the
/// theme transition, the 5.4 default look is framed once more as one wreath around both displays ("All Displays
/// Together", PO-1), and the "Classic 2003" and "Bright Glow" looks are checked the same way. Everything closes itself.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LiveThemeProbe : IClassFixture<RealLights>
{
    /// <summary>The glow is checked on a display only when at least this many wallpaper pixels around its top strip are uncovered.</summary>
    private const int MinimumGlowPixels = 10_000;

    private static readonly string[] ThemeNames = ["Halloween", "Christmas 1", "Winter Wonderland", "Holiday Party"];
    private static readonly LayerMode[] Modes = [LayerMode.BehindIcons, LayerMode.InFrontOfIcons, LayerMode.OnTop];

    private readonly RealLights lights;
    private readonly ITestOutputHelper output;
    private long revision;

    public LiveThemeProbe(RealLights lights, ITestOutputHelper output)
    {
        this.lights = lights;
        this.output = output;
    }

    [LiveFact]
    public async Task ShowsEachThemeExactlyInEveryModeOnEveryDisplay()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        RectI screen = displays.Select(d => d.Bounds).Aggregate((a, b) => a.Union(b));
        string folder = LiveSupport.OutputFolder("themes");
        ScreenShot before = ScreenShot.Take(screen);
        DesktopCapture.SavePng(Path.Combine(folder, "0-before.png"), before.Pixels, screen.Width, screen.Height, reduce: 4);
        output.WriteLine($"Displays: {string.Join("; ", displays.Select(d => $"{d.DeviceId} {d.Bounds} work {d.WorkArea} {d.Dpi} DPI"))}.");

        var log = new RecordingLog();
        var failures = new List<string>();
        using LightsPresenter presenter = LiveSupport.CreatePresenter(lights, new MusicEventHub(), log);
        try
        {
            presenter.Start();
            foreach (string name in ThemeNames)
            {
                ThemeDefinition theme = lights.Theme(name);
                foreach (LayerMode mode in Modes)
                {
                    // The look of the theme at the default settings, held still (Don't Flash) so every pixel is known.
                    LightsScene scene = Next(lights.Scene(theme, displays, mode, r => r with { Pattern = FlashPatternId.DontFlash }));
                    CheckScene(presenter, scene, mode, $"{Slug(name)}-{mode}", displays, before, folder, failures);
                }

                PlayTheme(presenter, theme, displays, screen, folder);
            }

            foreach (LayerMode mode in Modes)
            {
                LightsScene wreath = Next(lights.Scene(lights.Theme("Christmas 1"), displays, mode, r => r with { Pattern = FlashPatternId.DontFlash, Frame = FrameMode.AllDisplaysTogether }));
                CheckScene(presenter, wreath, mode, $"Wreath-{mode}", displays, before, folder, failures);
            }

            // The other Looks (PRODUCT-SPEC D12): "Classic 2003" (crisp pixels, no glow) and "Bright Glow".
            LightsScene classic = Next(lights.Scene(lights.Theme("Christmas 1"), displays, LayerMode.BehindIcons, r => r with { Pattern = FlashPatternId.DontFlash, Pixels = SpriteStyle.Crisp, Glow = GlowLevel.Off, SmoothFading = false }));
            CheckScene(presenter, classic, LayerMode.BehindIcons, "Classic-2003-look", displays, before, folder, failures);
            LightsScene bright = Next(lights.Scene(lights.Theme("Halloween"), displays, LayerMode.BehindIcons, r => r with { Pattern = FlashPatternId.DontFlash, Glow = GlowLevel.Bright }));
            CheckScene(presenter, bright, LayerMode.BehindIcons, "Bright-Glow-look", displays, before, folder, failures);
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: true).WaitAsync(TimeSpan.FromSeconds(5));
        }

        LiveSupport.WriteLog(log, output);
        output.WriteLine($"Captures: {folder}");
        Assert.Equal(0, LiveSupport.CountLayerWindows());
        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string Slug(string name) => name.Replace(' ', '-');

    /// <summary>The first few opaque sprite pixels the capture shows differently ("x,y expected/seen").</summary>
    private static string FirstDifferences(ScreenShot shot, PremultipliedImage image, PointI position)
    {
        var differences = new List<string>();
        for (int i = 0; i < image.Pixels.Length && differences.Count < 4; i++)
        {
            (int x, int y) = (i % image.Width, i / image.Width);
            uint expected = image.Pixels[i];
            if (expected >> 24 == 0xFF && shot.Contains(position.X + x, position.Y + y)
                && ScreenExpectations.Distance(expected, shot.At(position.X + x, position.Y + y)) > ScreenExpectations.OpaqueTolerance)
            {
                differences.Add($"{x},{y} {expected & 0xFFFFFF:X6}/{shot.At(position.X + x, position.Y + y) & 0xFFFFFF:X6}");
            }
        }

        return string.Join(" ", differences);
    }

    private LightsScene Next(LightsScene scene) => scene with { Revision = ++revision };

    /// <summary>Shows a still scene in a mode and checks the windows, every bulb, the glow and that the lights take no clicks.</summary>
    private void CheckScene(LightsPresenter presenter, LightsScene scene, LayerMode mode, string label, IReadOnlyList<DisplayInfo> displays, ScreenShot before, string folder, List<string> failures)
    {
        ScreenShot shot = Show(presenter, scene, mode, displays, failures);
        SaveShots(folder, label, shot, displays);
        LiveSupport.CheckWindows(presenter.Diagnostics, mode, displays.Count, output);
        CheckBulbs(scene, shot, mode, label, failures);
        if (mode == LayerMode.BehindIcons)
        {
            CheckGlow(scene, before, shot, label, failures);
        }

        // The lights never take the focus, and clicks pass through: the window under every corner bulb is never one of ours.
        if (User32.ClassNameOf(TestWindows.GetForegroundWindow()) == "HolidayLights.BulbLayer")
        {
            failures.Add($"{label}: a bulb layer became the foreground window.");
        }

        foreach (BulbPlacement corner in scene.Layout.Placements.Where(p => p.IsCorner))
        {
            if (LiveSupport.IsLayerAt((corner.Bounds.Left + corner.Bounds.Right) / 2, (corner.Bounds.Top + corner.Bounds.Bottom) / 2))
            {
                failures.Add($"{label}: the bulb layer takes the clicks at {corner.Bounds}.");
            }
        }
    }

    /// <summary>Applies a scene without transition, waits until the presenter shows it in the mode, lets DWM settle and captures the screen.</summary>
    private ScreenShot Show(LightsPresenter presenter, LightsScene scene, LayerMode mode, IReadOnlyList<DisplayInfo> displays, List<string> failures)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        presenter.Apply(scene, SceneTransition.None);
        bool shown = LiveSupport.WaitFor(
            () => presenter.Diagnostics.SceneRevision == scene.Revision && LiveSupport.AllShown(presenter, displays.Count, mode), TimeSpan.FromSeconds(20));
        long ready = watch.ElapsedMilliseconds;
        if (!shown)
        {
            failures.Add($"{mode}: revision {scene.Revision} was not shown: {LiveSupport.Describe(presenter.Status)}.");
        }

        Thread.Sleep(400);
        ScreenShot shot = ScreenShot.Take(displays.Select(d => d.Bounds).Aggregate((a, b) => a.Union(b)));
        LightsDiagnostics diagnostics = presenter.Diagnostics;
        output.WriteLine(
            $"{mode}: shown after {ready} ms (preparation {diagnostics.LastPreparationMilliseconds:F0} ms), {scene.Layout.Placements.Count} placements, "
            + $"{diagnostics.Surfaces} surfaces, {diagnostics.Visuals} visuals, {diagnostics.SkippedBulbs} skipped.");
        return shot;
    }

    /// <summary>Every bulb must show its Core sprite at its Core position, pixel for pixel (where no other window covers it).</summary>
    private void CheckBulbs(LightsScene scene, ScreenShot shot, LayerMode mode, string label, List<string> failures)
    {
        IFlashSequencer sequencer = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        ReadOnlySpan<BulbVisualState> states = sequencer.Current;
        int uncovered = 0;
        int exact = 0;
        long opaquePixels = 0;
        long matchingPixels = 0;
        var misses = new List<string>();
        foreach (BulbPlacement placement in scene.Layout.Placements)
        {
            RectI bounds = placement.Bounds;
            if (mode != LayerMode.OnTop && LiveSupport.IsCoveredByWindow((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2))
            {
                continue;
            }

            uncovered++;
            int frame = ScreenExpectations.ShownFrame(sequencer.GetBulb(placement.Ordinal), states[placement.Ordinal]);
            (PremultipliedImage image, PointI position) = ScreenExpectations.Sprite(lights, scene, placement, frame);
            (int opaque, int matching) = ScreenExpectations.Compare(shot, image, position);
            opaquePixels += opaque;
            matchingPixels += matching;
            if (matching >= opaque * 0.98)
            {
                exact++;
            }
            else if (misses.Count < 6)
            {
                misses.Add($"#{placement.Ordinal} {placement.BulbId} {placement.Slot} frame {frame} at {position}: {matching}/{opaque} ({FirstDifferences(shot, image, position)})");
            }
        }

        // Desktop icons lie above the lights behind the icons; nothing but the taskbars lies above them on top.
        double required = mode switch { LayerMode.OnTop => 0.995, LayerMode.InFrontOfIcons => 0.98, _ => 0.85 };
        output.WriteLine(
            $"  {label}: {exact} of {uncovered} uncovered bulbs exact ({scene.Layout.Placements.Count} placed); opaque pixels {matchingPixels:N0} of {opaquePixels:N0} "
            + $"({100.0 * matchingPixels / Math.Max(1, opaquePixels):F2} %).{(misses.Count > 0 ? " Differences: " + string.Join("; ", misses) : string.Empty)}");
        if (uncovered > 0 && exact < uncovered * required)
        {
            failures.Add($"{label}: only {exact} of {uncovered} uncovered bulbs show their sprite exactly (required {required:P1}).");
        }
    }

    /// <summary>
    /// Around the top strip of each display the glow must add light to the wallpaper: the capture equals the capture without
    /// lights plus the Core's glow halos of the lit bulbs (Soft = 0.55), wherever no bulb pixel lies (PRODUCT-SPEC 5.9).
    /// </summary>
    private void CheckGlow(LightsScene scene, ScreenShot before, ScreenShot shot, string label, List<string> failures)
    {
        IFlashSequencer sequencer = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        float intensity = GlowLevels.Intensity(scene.Effects.Glow);
        foreach (DisplayLayout display in scene.Layout.Displays)
        {
            RectI band = RectI.FromXYWH(display.Target.Area.Left, display.Target.Area.Top, display.Target.Area.Width, 160);
            var glow = new float[band.Width * band.Height * 3];
            var covered = new bool[band.Width * band.Height];
            foreach (BulbPlacement placement in scene.Layout.Placements.Where(p => p.DisplayId == display.Target.DisplayId && p.Bounds.Top < band.Bottom))
            {
                FlashBulbInfo info = sequencer.GetBulb(placement.Ordinal);
                BulbVisualState state = sequencer.Current[placement.Ordinal];
                (PremultipliedImage sprite, PointI position) = ScreenExpectations.Sprite(lights, scene, placement, ScreenExpectations.ShownFrame(info, state));
                Stamp(covered, band, sprite, position);
                if (info.Kind != BulbAnimationKind.LightBulb || state.Glow <= 0 || !lights.Catalog.TryGetBulb(placement.BulbId, out IBulb? bulb))
                {
                    continue;
                }

                if (lights.Sprites.GetGlow(bulb!, placement.Slot, placement.Flavor, display.Target.Scale) is { } halo)
                {
                    (_, PointI litAt) = ScreenExpectations.Sprite(lights, scene, placement, info.LitFrame);
                    Add(glow, band, halo.Image, litAt.X + halo.OffsetX, litAt.Y + halo.OffsetY, intensity * state.Glow, display.Target.Area);
                }
            }

            // Application windows hide the desktop layer: skip 16 x 16 blocks whose centre another window covers.
            const int Block = 16;
            var hidden = new bool[(band.Width / Block) + 1, (band.Height / Block) + 1];
            for (int by = 0; by < hidden.GetLength(1); by++)
            {
                for (int bx = 0; bx < hidden.GetLength(0); bx++)
                {
                    hidden[bx, by] = LiveSupport.IsCoveredByWindow(Math.Min(band.Right - 1, band.Left + (bx * Block) + (Block / 2)), Math.Min(band.Bottom - 1, band.Top + (by * Block) + (Block / 2)));
                }
            }

            int compared = 0;
            int matching = 0;
            int lit = 0;
            for (int y = band.Top; y < band.Bottom; y++)
            {
                for (int x = band.Left; x < band.Right; x++)
                {
                    int i = ((y - band.Top) * band.Width) + (x - band.Left);
                    if (covered[i] || hidden[(x - band.Left) / Block, (y - band.Top) / Block])
                    {
                        continue;
                    }

                    uint seen = shot.At(x, y);
                    uint expected = AddLight(before.At(x, y), glow[3 * i], glow[(3 * i) + 1], glow[(3 * i) + 2]);
                    compared++;
                    lit += glow[3 * i] + glow[(3 * i) + 1] + glow[(3 * i) + 2] >= 6 ? 1 : 0;
                    matching += ScreenExpectations.Distance(seen, expected) <= 3 ? 1 : 0;
                }
            }

            if (compared < MinimumGlowPixels)
            {
                output.WriteLine($"  {label} glow on {display.Target.DisplayId}: not checked, application windows cover the top strip.");
                continue;
            }

            double share = (double)matching / compared;
            output.WriteLine($"  {label} glow on {display.Target.DisplayId}: {matching:N0} of {compared:N0} pixels around the top strip equal wallpaper + glow ({share:P1}; {lit:N0} visibly lit).");
            if (share < 0.9)
            {
                failures.Add($"{label}: the glow on {display.Target.DisplayId} matches only {share:P1} of the pixels.");
            }
        }
    }

    /// <summary>Marks the pixels a sprite covers (any alpha) in a band.</summary>
    private static void Stamp(bool[] covered, RectI band, PremultipliedImage sprite, PointI position)
    {
        for (int y = 0; y < sprite.Height; y++)
        {
            for (int x = 0; x < sprite.Width; x++)
            {
                int bx = position.X + x - band.Left;
                int by = position.Y + y - band.Top;
                if (bx >= 0 && by >= 0 && bx < band.Width && by < band.Height && sprite.Pixels[(y * sprite.Width) + x] >> 24 != 0)
                {
                    covered[(by * band.Width) + bx] = true;
                }
            }
        }
    }

    /// <summary>Adds a premultiplied additive halo at an opacity, clipped to the display (the layer window).</summary>
    private static void Add(float[] glow, RectI band, PremultipliedImage halo, int left, int top, float opacity, RectI display)
    {
        for (int y = 0; y < halo.Height; y++)
        {
            for (int x = 0; x < halo.Width; x++)
            {
                int sx = left + x;
                int sy = top + y;
                if (sx < band.Left || sy < band.Top || sx >= band.Right || sy >= band.Bottom || sx < display.Left || sx >= display.Right)
                {
                    continue;
                }

                uint pixel = halo.Pixels[(y * halo.Width) + x];
                int i = 3 * (((sy - band.Top) * band.Width) + (sx - band.Left));
                glow[i] += ((pixel >> 16) & 0xFF) * opacity;
                glow[i + 1] += ((pixel >> 8) & 0xFF) * opacity;
                glow[i + 2] += (pixel & 0xFF) * opacity;
            }
        }
    }

    private static uint AddLight(uint pixel, float red, float green, float blue)
    {
        static uint Channel(uint value, float light) => (uint)Math.Min(255, Math.Round(value + light));
        return 0xFF000000 | Channel((pixel >> 16) & 0xFF, red) << 16 | Channel((pixel >> 8) & 0xFF, green) << 8 | Channel(pixel & 0xFF, blue);
    }

    /// <summary>
    /// The theme with its own pattern after the theme transition (old lights fade out over 250 ms, then the short power-up wave
    /// runs clockwise from the top-left corner), filmed along the top strip of each display: one row per moment.
    /// </summary>
    private void PlayTheme(LightsPresenter presenter, ThemeDefinition theme, IReadOnlyList<DisplayInfo> displays, RectI screen, string folder)
    {
        const int BandHeight = 100;
        int[] moments = [100, 300, 500, 700, 900, 1100, 1300, 1600, 2500];
        LightsScene scene = Next(lights.Scene(theme, displays, LayerMode.BehindIcons));
        var band = RectI.FromXYWH(screen.Left, displays.Min(d => d.WorkArea.Top), screen.Width, BandHeight);
        var rows = new List<ScreenShot>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        presenter.Apply(scene, SceneTransition.ThemeTransition);
        foreach (int at in moments)
        {
            while (watch.ElapsedMilliseconds < at)
            {
                Thread.Sleep(2);
            }

            rows.Add(ScreenShot.Take(band));
        }

        foreach (DisplayInfo display in displays)
        {
            int width = display.WorkArea.Width;
            var film = new uint[width * BandHeight * rows.Count];
            for (int row = 0; row < rows.Count; row++)
            {
                uint[] strip = DesktopCapture.Crop(rows[row].Pixels, band.Width, RectI.FromXYWH(display.WorkArea.Left - band.Left, display.WorkArea.Top - band.Top, width, BandHeight));
                strip.CopyTo(film, row * width * BandHeight);
            }

            DesktopCapture.SavePng(Path.Combine(folder, $"{Slug(theme.Name)}-transition-display{display.Number}.png"), film, width, BandHeight * rows.Count, reduce: 2);
        }

        LightsDiagnostics diagnostics = presenter.Diagnostics;
        output.WriteLine(
            $"{theme.Name} plays {FlashPatterns.DisplayName(scene.Flash.Pattern)} at {scene.Interval * 60} ms after the theme transition (rows at {string.Join(", ", moments)} ms): "
            + $"clock running {diagnostics.ClockRunning}, {diagnostics.PeriodicLightBulbs} light bulbs on repeating animations, {diagnostics.Steps} steps processed so far.");
    }

    private static void SaveShots(string folder, string label, ScreenShot shot, IReadOnlyList<DisplayInfo> displays)
    {
        RectI screen = shot.Area;
        DesktopCapture.SavePng(Path.Combine(folder, $"{label}.png"), shot.Pixels, screen.Width, screen.Height, reduce: 4);
        foreach (DisplayInfo display in displays)
        {
            // 1:1 crops of the top-left and bottom-right corners of each display's work area.
            int size = Math.Min(1200, display.WorkArea.Width / 3);
            RectI topLeft = RectI.FromXYWH(display.WorkArea.Left - screen.Left, display.WorkArea.Top - screen.Top, size, size / 2);
            RectI bottomRight = RectI.FromXYWH(display.WorkArea.Right - size - screen.Left, display.WorkArea.Bottom - (size / 2) - screen.Top, size, size / 2);
            DesktopCapture.SavePng(Path.Combine(folder, $"{label}-display{display.Number}-top-left.png"), DesktopCapture.Crop(shot.Pixels, screen.Width, topLeft), size, size / 2);
            DesktopCapture.SavePng(Path.Combine(folder, $"{label}-display{display.Number}-bottom-right.png"), DesktopCapture.Crop(shot.Pixels, screen.Width, bottomRight), size, size / 2);
        }
    }
}
