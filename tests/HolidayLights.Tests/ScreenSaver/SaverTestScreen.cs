using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>Screen helpers of the screen saver's visual checks: captures, PNG files, DPI awareness and a key press for the saver.</summary>
internal static unsafe partial class SaverTestScreen
{
    private const uint KeyDown = 0x0100;
    private const nint SpaceKey = 0x20;
    private const nint SpaceKeyData = 0x00390001;

    /// <summary>Makes the test host PerMonitorV2 like the app (its manifest); call before WPF starts in the process.</summary>
    public static void UsePhysicalPixels() => SetProcessDpiAwarenessContext(-4);

    /// <summary>The screen in physical pixels (BitBlt with CAPTUREBLT), opaque.</summary>
    /// <param name="area">The rectangle of the virtual screen.</param>
    /// <returns>The pixels, top-down.</returns>
    public static uint[] Capture(RectI area)
    {
        var header = new CaptureHeader { Size = (uint)sizeof(CaptureHeader), Width = area.Width, Height = -area.Height, Planes = 1, BitCount = 32 };
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        void* bits;
        nint bitmap = CreateDIBSection(screen, &header, 0, &bits, 0, 0);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, 0x00CC0020 | 0x40000000);
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
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>Counts the pixels that are not black.</summary>
    public static int CountLit(uint[] pixels) => pixels.Count(p => (p & 0x00FFFFFF) != 0);

    /// <summary>Writes premultiplied BGRA pixels as a PNG file.</summary>
    public static void Save(string path, uint[] pixels, int width, int height)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4)));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Posts a key press to the first visible saver window of this process ("Holiday Lights"), as a user would type.</summary>
    /// <returns>True when a saver window was found.</returns>
    public static bool PressKeyInSaverWindow()
    {
        uint process = (uint)Environment.ProcessId;
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            var title = new char[32];
            int length = GetWindowText(hwnd, title, title.Length);
            if (owner == process && IsWindowVisible(hwnd) && new string(title, 0, length) == "Holiday Lights")
            {
                found = hwnd;
                return false;
            }

            return true;
        }, 0);
        return found != 0 && PostMessage(found, KeyDown, SpaceKey, SpaceKeyData);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CaptureHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, CaptureHeader* header, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
