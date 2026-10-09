using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Shell;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>Live-test helpers: the real displays in physical pixels and DWM-composed screen captures (BitBlt SRCCOPY | CAPTUREBLT).</summary>
internal static unsafe partial class DesktopCapture
{
    private const uint SourceCopy = 0x00CC0020;
    private const uint CaptureLayered = 0x40000000;

    /// <summary>Makes the calling thread PerMonitorV2 so coordinates are physical.</summary>
    public static void UsePhysicalPixels() => User32.SetThreadDpiAwarenessContext(Win32Constants.DpiAwarenessContextPerMonitorAwareV2);

    /// <summary>The connected displays as the platform would report them (device name as id; the primary is display 1).</summary>
    public static IReadOnlyList<DisplayInfo> Displays()
    {
        UsePhysicalPixels();
        IReadOnlyList<MonitorState> monitors = MonitorTopology.Capture();
        var ordered = monitors.OrderByDescending(m => m.Bounds.Left == 0 && m.Bounds.Top == 0).ThenBy(m => m.Bounds.Left).ToList();
        return [.. ordered.Select((m, i) => new DisplayInfo
        {
            DeviceId = m.DeviceName,
            DeviceName = m.DeviceName,
            Number = i + 1,
            Bounds = m.Bounds,
            WorkArea = m.WorkArea,
            Dpi = m.Dpi,
            IsPrimary = i == 0,
        })];
    }

    /// <summary>Captures a screen rectangle (opaque BGRA, top-down).</summary>
    public static uint[] Capture(RectI area)
    {
        UsePhysicalPixels();
        var header = new BITMAPINFOHEADER
        {
            Size = (uint)sizeof(BITMAPINFOHEADER),
            Width = area.Width,
            Height = -area.Height,
            Planes = 1,
            BitCount = 32,
        };
        nint screen = GetDC(0);
        nint memory = Gdi32.CreateCompatibleDC(screen);
        void* bits;
        nint bitmap = Gdi32.CreateDIBSection(screen, &header, 0, &bits, 0, 0);
        nint previous = Gdi32.SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, SourceCopy | CaptureLayered);
            Gdi32.GdiFlush();
            var pixels = new uint[area.Width * area.Height];
            new ReadOnlySpan<uint>(bits, pixels.Length).CopyTo(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] |= 0xFF000000;
            }

            return pixels;
        }
        finally
        {
            Gdi32.SelectObject(memory, previous);
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>Saves BGRA pixels as a PNG, optionally reduced by an integer factor (box filter).</summary>
    public static void SavePng(string path, uint[] pixels, int width, int height, int reduce = 1)
    {
        int w = width / reduce;
        int h = height / reduce;
        var output = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                long r = 0, g = 0, b = 0;
                for (int dy = 0; dy < reduce; dy++)
                {
                    for (int dx = 0; dx < reduce; dx++)
                    {
                        uint p = pixels[(y * reduce + dy) * width + x * reduce + dx];
                        r += (p >> 16) & 0xFF;
                        g += (p >> 8) & 0xFF;
                        b += p & 0xFF;
                    }
                }

                int n = reduce * reduce;
                output[y * w + x] = 0xFF000000 | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
            }
        }

        BitmapSource source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, output, w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Copies a sub-rectangle of a capture.</summary>
    public static uint[] Crop(uint[] pixels, int width, RectI area)
    {
        var result = new uint[area.Width * area.Height];
        for (int y = 0; y < area.Height; y++)
        {
            Array.Copy(pixels, (area.Top + y) * width + area.Left, result, y * area.Width, area.Width);
        }

        return result;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}

/// <summary>A test that touches the real desktop: it runs only when <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c> (it shows bulbs for a few seconds and closes itself).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HOLIDAYLIGHTS_LIVE_TESTS") != "1")
        {
            Skip = "Live desktop test: set HOLIDAYLIGHTS_LIVE_TESTS=1 to run it.";
        }
    }
}
