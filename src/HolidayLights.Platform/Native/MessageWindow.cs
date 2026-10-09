using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HolidayLights.Platform.Native;

/// <summary>
/// A hidden, never-shown top-level window on the thread that creates it. Unlike a message-only window it receives
/// broadcasts (<c>WM_SETTINGCHANGE</c>, <c>WM_DISPLAYCHANGE</c>, <c>TaskbarCreated</c>); it also receives timers, hot keys
/// and posted messages. The creating thread must pump messages (the WPF dispatcher does).
/// </summary>
internal sealed unsafe class MessageWindow : IDisposable
{
    private const string ClassName = "HolidayLights.Platform.MessageWindow";
    private static readonly Lazy<nint> ModuleHandle = new(RegisterWindowClass, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Handler handler;
    private readonly IAppLog log;
    private readonly string source;
    private GCHandle self;

    /// <summary>Creates the window on the calling thread.</summary>
    /// <param name="name">Window title (diagnostics only).</param>
    /// <param name="handler">Handles messages; return true when handled (with the result to return).</param>
    /// <param name="log">Receives exceptions thrown by <paramref name="handler"/>.</param>
    /// <param name="source">Log source of the owner.</param>
    /// <exception cref="Win32Exception">The window cannot be created.</exception>
    public MessageWindow(string name, Handler handler, IAppLog log, string source)
    {
        this.handler = handler;
        this.log = log;
        this.source = source;
        OwnerThreadId = NativeMethods.GetCurrentThreadId();
        self = GCHandle.Alloc(this);
        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE, ClassName, name, NativeMethods.WS_POPUP,
            0, 0, 0, 0, 0, 0, ModuleHandle.Value, GCHandle.ToIntPtr(self));
        if (Handle == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (self.IsAllocated)
            {
                self.Free();
            }

            throw new Win32Exception(error, $"Cannot create the {name} window.");
        }
    }

    /// <summary>Handles one message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">Its wParam.</param>
    /// <param name="lParam">Its lParam.</param>
    /// <param name="result">The value to return when handled.</param>
    /// <returns>True when handled (the default window procedure is skipped).</returns>
    internal delegate bool Handler(uint message, nint wParam, nint lParam, out nint result);

    /// <summary>The window handle (0 after the window was destroyed).</summary>
    public nint Handle { get; private set; }

    /// <summary>The thread that owns the window.</summary>
    public uint OwnerThreadId { get; }

    /// <summary>Starts or restarts a timer (a restarted timer waits its full interval again: a natural debounce).</summary>
    /// <param name="id">Timer id (non-zero).</param>
    /// <param name="interval">Interval.</param>
    public void StartTimer(nuint id, TimeSpan interval)
    {
        if (Handle != 0)
        {
            NativeMethods.SetTimer(Handle, id, (uint)Math.Max(1, interval.TotalMilliseconds), 0);
        }
    }

    /// <summary>Stops a timer (no-op when it is not running).</summary>
    /// <param name="id">Timer id.</param>
    public void StopTimer(nuint id)
    {
        if (Handle != 0)
        {
            NativeMethods.KillTimer(Handle, id);
        }
    }

    /// <summary>Posts a message to the window (any thread).</summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">Its wParam.</param>
    /// <param name="lParam">Its lParam.</param>
    /// <returns>True when it was queued.</returns>
    public bool Post(uint message, nint wParam = 0, nint lParam = 0) =>
        Handle != 0 && NativeMethods.PostMessage(Handle, message, wParam, lParam);

    /// <summary>Destroys the window (on another thread: asks the owner thread to do it).</summary>
    public void Dispose()
    {
        nint hwnd = Handle;
        if (hwnd == 0)
        {
            return;
        }

        if (NativeMethods.GetCurrentThreadId() == OwnerThreadId)
        {
            NativeMethods.DestroyWindow(hwnd);
        }
        else
        {
            NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, 0, 0);
        }
    }

    private static nint RegisterWindowClass()
    {
        nint module = NativeMethods.GetModuleHandle(null);
        fixed (char* className = ClassName)
        {
            var windowClass = new WindowClassEx
            {
                Size = (uint)sizeof(WindowClassEx),
                WindowProc = &WindowProc,
                Instance = module,
                ClassName = className,
            };

            const int ErrorClassAlreadyExists = 1410;
            if (NativeMethods.RegisterClassEx(&windowClass) == 0 && Marshal.GetLastPInvokeError() != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot register the message window class.");
            }
        }

        return module;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == NativeMethods.WM_NCCREATE)
        {
            // CREATESTRUCTW.lpCreateParams is the first member.
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA, *(nint*)lParam);
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }

        nint cookie = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA);
        if (cookie == 0 || GCHandle.FromIntPtr(cookie).Target is not MessageWindow window)
        {
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }

        if (message == NativeMethods.WM_NCDESTROY)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA, 0);
            window.Handle = 0;
            window.self.Free();
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }

        try
        {
            if (window.handler(message, wParam, lParam, out nint result))
            {
                return result;
            }
        }
        catch (Exception e)
        {
            // Never let an exception cross the native window procedure: it would end the process.
            window.log.Error(window.source, $"Unexpected error while handling window message 0x{message:X4}.", e);
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
