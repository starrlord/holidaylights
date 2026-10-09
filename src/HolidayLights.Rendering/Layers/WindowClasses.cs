using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Threading;

namespace HolidayLights.Rendering.Layers;

/// <summary>
/// The two window classes of the Lights thread: <c>HolidayLights.BulbLayer</c> (layers, pill and Identify: never hit-tested,
/// never activated, never painted by GDI) and <c>HolidayLights.LightsWatcher</c> (the hidden top-level window that receives
/// <c>TaskbarCreated</c>, display, work-area, DPI and power broadcasts). Registered once per process.
/// </summary>
internal static unsafe class WindowClasses
{
    /// <summary>The class of every visible window (the platform's full-screen check skips it).</summary>
    public const string LayerClass = "HolidayLights.BulbLayer";

    /// <summary>The class of the hidden watcher window.</summary>
    public const string WatcherClass = "HolidayLights.LightsWatcher";

    private static readonly Lock RegistrationLock = new();
    private static bool registered;
    private static uint taskbarCreatedMessage;

    /// <summary>The module handle the classes are registered with.</summary>
    public static nint Instance { get; private set; }

    /// <summary>Registers both classes (idempotent, thread-safe).</summary>
    public static void EnsureRegistered()
    {
        lock (RegistrationLock)
        {
            if (registered)
            {
                return;
            }

            Instance = Kernel32.GetModuleHandleW(null);
            taskbarCreatedMessage = User32.RegisterWindowMessageW("TaskbarCreated");
            Register(LayerClass, &LayerProcedure);
            Register(WatcherClass, &WatcherProcedure);
            registered = true;
        }
    }

    private static void Register(string name, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> procedure)
    {
        fixed (char* className = name)
        {
            var windowClass = new WNDCLASSEXW
            {
                Size = (uint)sizeof(WNDCLASSEXW),
                WndProc = procedure,
                Instance = Instance,
                ClassName = className,
            };

            // A second registration (another presenter in the same process) fails with ERROR_CLASS_ALREADY_EXISTS: fine.
            User32.RegisterClassExW(&windowClass);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint LayerProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WindowMessages.NcHitTest:
                return WindowMessages.HitTestTransparent;
            case WindowMessages.MouseActivate:
                return WindowMessages.MouseActivateNoActivate;
            case WindowMessages.EraseBackground:
                return 1;
            case WindowMessages.Paint:
                User32.ValidateRect(hwnd, null);
                return 0;
            case WindowMessages.DpiChanged:
                LightsThreadContext.Guard(sink => sink.OnWindowDpiChanged(hwnd));
                return 0;
            case WindowMessages.Destroy:
                LightsThreadContext.Guard(sink => sink.OnWindowDestroyed(hwnd));
                return 0;
            case WindowMessages.NcDestroy:
                // Window properties must be removed before WM_NCDESTROY returns, also when Explorer destroys the owner.
                User32.RemovePropW(hwnd, LayerWindow.NonRudeProperty);
                return User32.DefWindowProcW(hwnd, message, wParam, lParam);
            default:
                return User32.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WatcherProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == taskbarCreatedMessage && message != 0)
        {
            LightsThreadContext.Guard(sink => sink.OnShellEvent(ShellEvent.TaskbarCreated));
            return 0;
        }

        switch (message)
        {
            case WindowMessages.DisplayChange:
                LightsThreadContext.Guard(sink => sink.OnShellEvent(ShellEvent.DisplayChange));
                break;
            case WindowMessages.SettingChange when (uint)wParam == WindowMessages.SetWorkArea:
                LightsThreadContext.Guard(sink => sink.OnShellEvent(ShellEvent.WorkAreaChange));
                break;
            case WindowMessages.DpiChanged:
                LightsThreadContext.Guard(sink => sink.OnShellEvent(ShellEvent.DpiChange));
                return 0;
            case WindowMessages.PowerBroadcast when (uint)wParam is WindowMessages.PowerResumeAutomatic or WindowMessages.PowerResumeSuspend:
                LightsThreadContext.Guard(sink => sink.OnShellEvent(ShellEvent.Resumed));
                return 1;
            case WindowMessages.Destroy:
                LightsThreadContext.Guard(sink => sink.OnWindowDestroyed(hwnd));
                return 0;
        }

        return User32.DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
