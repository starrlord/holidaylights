using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.Shell;

/// <summary>
/// <c>--render-test &lt;dir&gt;</c> (ARCHITECTURE 8, CONTRACTS 6.2): the current scene of every display rendered with the
/// CPU compositor at zoom 1 (the same layout, sprites, brightness and additive glow as the desktop; light bulbs lit, frame
/// 0), one PNG per display (<c>display-&lt;n&gt;.png</c>, physical pixels, night backdrop and taskbar band) and a
/// description of the scene (<c>render-test.txt</c>). No windows are created; settings are only read.
/// </summary>
public static class RenderTest
{
    /// <summary>How long the add-on index may take (a first, cold start indexes 1,501 bulbs).</summary>
    private static readonly TimeSpan IndexTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Renders the displays into a folder.</summary>
    /// <param name="host">The composition root of a render-test session.</param>
    /// <param name="folder">The output folder (created when missing).</param>
    /// <returns>The process exit code: 0 when every display was written.</returns>
    public static int Run(AppHost host, string folder)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Directory.CreateDirectory(folder);
        if (!host.CatalogIndexing.Wait(IndexTimeout))
        {
            host.Log.Warn("Shell.RenderTest", "The bulb index was not complete; rendering with what is known.");
        }

        LightsScene scene = host.Lights.Scene;
        var renderer = new ScenePreviewRenderer(host.Sprites, host.Compositor);
        foreach (DisplayScene display in scene.Displays)
        {
            PremultipliedImage image = renderer.RenderDisplay(new ScenePreviewRequest { Scene = scene, Bulbs = host.Bulbs }, display.Display.DeviceId);
            WritePng(image, Path.Combine(folder, FileName(display.Display)));
        }

        File.WriteAllText(Path.Combine(folder, "render-test.txt"), Describe(scene), Encoding.UTF8);
        host.Log.Info("Shell.RenderTest", $"Rendered {scene.Displays.Count} display(s).");
        return 0;
    }

    /// <summary>The PNG file of a display.</summary>
    /// <param name="display">The display.</param>
    /// <returns><c>display-&lt;number&gt;.png</c>.</returns>
    public static string FileName(DisplayInfo display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return string.Create(CultureInfo.InvariantCulture, $"display-{display.Number}.png");
    }

    /// <summary>Writes a premultiplied picture as a PNG with straight alpha.</summary>
    /// <param name="image">The picture.</param>
    /// <param name="path">The file.</param>
    public static void WritePng(PremultipliedImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(path);
        BitmapSource premultiplied = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(premultiplied, PixelFormats.Bgra32, null, 0)));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Describes a scene: the look, then per display its geometry, scale and strips.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>Plain text.</returns>
    public static string Describe(LightsScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var text = new StringBuilder();
        CultureInfo invariant = CultureInfo.InvariantCulture;
        text.AppendLine(invariant, $"Lights {(scene.LightsOn ? "on" : "off")}, {scene.RequestedLayer}, {scene.Layout.Mode}, {scene.Flash.Pattern}, interval {scene.Interval}, {scene.Effects.Pixels}, glow {scene.Effects.Glow}");
        foreach (DisplayScene entry in scene.Displays)
        {
            DisplayInfo display = entry.Display;
            text.AppendLine(invariant, $"Display {display.Number}: bounds {display.Bounds}, work area {display.WorkArea}, {display.Dpi} DPI, {(entry.Enabled ? "lights" : "no lights")}");
            DisplayLayout? layout = scene.Layout.Displays.FirstOrDefault(d => string.Equals(d.Target.DisplayId, display.DeviceId, StringComparison.Ordinal));
            if (layout is null)
            {
                continue;
            }

            text.AppendLine(invariant, $"  scale {layout.Target.Scale:0.###}");
            foreach (StripLayout strip in layout.Strips)
            {
                int corners = strip.Placements.Count(p => p.IsCorner);
                text.AppendLine(invariant, $"  {strip.Side}: {strip.Count} bulbs + {corners} corners, thickness {strip.Thickness}, gap {strip.Gap:0.####}, rect {strip.Rect}");
            }
        }

        return text.ToString();
    }
}
