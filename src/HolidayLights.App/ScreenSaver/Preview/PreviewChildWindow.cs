using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.App.ScreenSaver.Native;
using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;

namespace HolidayLights.App.ScreenSaver.Preview;

/// <summary>
/// The <c>/p &lt;hwnd&gt;</c> preview (PRODUCT-SPEC 6.2.1): a child of Windows' preview window, created on a thread whose DPI
/// awareness equals the parent's (otherwise Windows resets the process's awareness), showing the saver of the main
/// display simulated at its DIP size and drawn scaled down into the small rectangle (area-averaged art, black bars when
/// the shapes differ). No music, no input. It ends when the parent window goes away.
/// </summary>
internal sealed unsafe class PreviewChildWindow
{
    private const string LogSource = "ScreenSaver.Preview";
    private const string ClassName = "HolidayLights.ScreenSaver.Preview";
    private const nuint FrameTimer = 1;

    /// <summary>How often the thread timer checks that both windows still exist.</summary>
    private const uint WatchMilliseconds = 250;

    private static readonly Lazy<nint> RegisteredModule = new(RegisterWindowClass, LazyThreadSafetyMode.ExecutionAndPublication);

    [ThreadStatic]
    private static PreviewChildWindow? current;

    private readonly SaverServices services;
    private readonly AppSettings settings;
    private readonly DisplayInfo display;
    private readonly nint parent;
    private readonly SaverSpriteCache sprites = new();
    private nint handle;
    private nuint watchTimer;
    private SaverRun? run;
    private CpuSaverRenderer? renderer;
    private PremultipliedImage canvas = new(1, 1);
    private PointI frameOrigin;

    /// <summary>Prepares the preview.</summary>
    /// <param name="services">What it draws with.</param>
    /// <param name="settings">The settings to show.</param>
    /// <param name="display">The main display (simulated at its DIP size).</param>
    /// <param name="parent">Windows' preview window.</param>
    public PreviewChildWindow(SaverServices services, AppSettings settings, DisplayInfo display, nint parent)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(display);
        this.services = services;
        this.settings = settings;
        this.display = display;
        this.parent = parent;
    }

    /// <summary>Creates the child window and runs its message loop until it is destroyed (call on an STA thread).</summary>
    /// <returns>The exit code: 0, or 1 when the parent window is gone.</returns>
    /// <exception cref="Win32Exception">The window cannot be created.</exception>
    /// <remarks>Failures while it runs are logged and end it; it never outlives this call.</remarks>
    public int Run()
    {
        if (!SaverNativeMethods.IsWindow(parent))
        {
            services.Log.Warn(LogSource, "The preview window is gone.");
            return 1;
        }

        nint previous = SaverNativeMethods.SetThreadDpiAwarenessContext(SaverNativeMethods.GetWindowDpiAwarenessContext(parent));
        current = this;
        try
        {
            NativeRect client;
            SaverNativeMethods.GetClientRect(parent, &client);
            handle = SaverNativeMethods.CreateWindowEx(
                0, ClassName, "Holiday Lights", SaverNativeMethods.WS_CHILD | SaverNativeMethods.WS_VISIBLE | SaverNativeMethods.WS_CLIPCHILDREN,
                0, 0, client.Right - client.Left, client.Bottom - client.Top, parent, 0, RegisteredModule.Value, 0);
            if (handle == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot create the screen saver preview window.");
            }

            Prepare(client.Right - client.Left, client.Bottom - client.Top);
            SaverNativeMethods.SetTimer(handle, FrameTimer, (uint)(settings.Look.SmoothSaverMotion ? 33 : StepClock.TickMilliseconds), 0);
            watchTimer = SaverNativeMethods.SetTimer(0, 0, WatchMilliseconds, 0);
            NativeMessage message;
            while (SaverNativeMethods.GetMessage(&message, 0, 0, 0) > 0)
            {
                if (message.Hwnd == 0 && message.Message == SaverNativeMethods.WM_TIMER)
                {
                    if (WindowsGone())
                    {
                        services.Log.Info(LogSource, "The preview window went away; the preview ends.");
                        break;
                    }

                    continue;
                }

                SaverNativeMethods.TranslateMessage(&message);
                SaverNativeMethods.DispatchMessage(&message);
            }

            return 0;
        }
        finally
        {
            if (watchTimer != 0)
            {
                SaverNativeMethods.KillTimer(0, watchTimer);
                watchTimer = 0;
            }

            if (handle != 0)
            {
                // Setting up failed or the message loop broke off: the child must not outlive the preview.
                SaverNativeMethods.DestroyWindow(handle);
                handle = 0;
            }

            run?.Dispose();
            current = null;
            SaverNativeMethods.SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>
    /// True when Windows' preview window or the child is gone. Checked by a thread timer, which outlives both windows: when
    /// the process that owns the preview window is killed, the child disappears with it without a <c>WM_DESTROY</c> here,
    /// and its own timer stops, so nothing else would end the message loop.
    /// </summary>
    private bool WindowsGone() => !SaverNativeMethods.IsWindow(parent) || handle == 0 || !SaverNativeMethods.IsWindow(handle);

    private static nint RegisterWindowClass()
    {
        nint module = SaverNativeMethods.GetModuleHandle(null);
        fixed (char* className = ClassName)
        {
            var windowClass = new NativeWindowClass
            {
                Size = (uint)sizeof(NativeWindowClass),
                WindowProc = &WindowProc,
                Instance = module,
                ClassName = className,
            };

            const int ErrorClassAlreadyExists = 1410;
            if (SaverNativeMethods.RegisterClassEx(&windowClass) == 0 && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register the screen saver preview window class.");
            }
        }

        return module;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (current is { } preview && preview.Handle(hwnd, message, out nint result))
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            current?.services.Log.Error(LogSource, "The screen saver preview failed.", ex);
            SaverNativeMethods.DestroyWindow(hwnd);
            return 0;
        }

        return SaverNativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>Builds the scene of the main display and a renderer that fits the window.</summary>
    private void Prepare(int width, int height)
    {
        canvas = new PremultipliedImage(Math.Max(1, width), Math.Max(1, height));
        services.Compositor.Clear(canvas, RgbColor.Black.ToBgra32());
        uint seed = unchecked((uint)Stopwatch.GetTimestamp());
        SaverOptions options = SaverOptions.FromSettings(settings, seed);
        SaverDisplayPlan plan = new(display, ShowsContent: true, IsMain: true);
        long start = Stopwatch.GetTimestamp();
        SaverScene scene = services.CreateScenes([plan], options, DecodedPicture.Load(services.Pictures, options.Look.Picture), seed, start)[0];
        double zoom = Math.Min((double)canvas.Width / display.Bounds.Width, (double)canvas.Height / display.Bounds.Height);
        renderer = services.CreateRenderer(scene, zoom, sprites);
        frameOrigin = new PointI((canvas.Width - renderer.Frame.Width) / 2, (canvas.Height - renderer.Frame.Height) / 2);
        run = new SaverRun([scene], start);
    }

    private bool Handle(nint hwnd, uint message, out nint result)
    {
        result = 0;
        if (hwnd != handle)
        {
            return false;
        }

        switch (message)
        {
            case SaverNativeMethods.WM_TIMER:
                if (!SaverNativeMethods.IsWindow(parent))
                {
                    SaverNativeMethods.DestroyWindow(hwnd);
                    return true;
                }

                DrawFrame();
                SaverNativeMethods.InvalidateRect(hwnd, null, false);
                return true;
            case SaverNativeMethods.WM_PAINT:
                Present(hwnd);
                return true;
            case SaverNativeMethods.WM_ERASEBKGND:
                result = 1;
                return true;
            case SaverNativeMethods.WM_DESTROY:
                SaverNativeMethods.KillTimer(hwnd, FrameTimer);
                handle = 0;
                SaverNativeMethods.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    private void DrawFrame()
    {
        if (run is null || renderer is null)
        {
            return;
        }

        CpuSaverRenderer frame = renderer;
        run.Advance(Stopwatch.GetTimestamp(), _ => frame.ApplyStep());
        frame.Render(run.Progress);
        services.Compositor.Draw(canvas, frame.Frame, frameOrigin.X, frameOrigin.Y, 1f, CompositeMode.SourceOver);
    }

    private void Present(nint hwnd)
    {
        NativePaint paint;
        nint dc = SaverNativeMethods.BeginPaint(hwnd, &paint);
        try
        {
            var header = new NativeBitmapInfoHeader
            {
                Size = (uint)sizeof(NativeBitmapInfoHeader),
                Width = canvas.Width,
                Height = -canvas.Height,
                Planes = 1,
                BitCount = 32,
                Compression = SaverNativeMethods.BI_RGB,
            };
            fixed (uint* bits = canvas.Pixels)
            {
                SaverNativeMethods.SetDIBitsToDevice(dc, 0, 0, (uint)canvas.Width, (uint)canvas.Height, 0, 0, 0, (uint)canvas.Height, bits, &header,
                    SaverNativeMethods.DIB_RGB_COLORS);
            }
        }
        finally
        {
            SaverNativeMethods.EndPaint(hwnd, &paint);
        }
    }
}
