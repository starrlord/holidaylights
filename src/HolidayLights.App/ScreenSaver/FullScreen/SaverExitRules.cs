using HolidayLights.App.ScreenSaver.Native;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// What ends the running saver (PRODUCT-SPEC 6.2.1; 5.4 <c>DefScreenSaverProc</c>): any key, any mouse button, the wheel,
/// a touch or pen contact, pointer movement of more than 4 DIPs (Manhattan distance from where the pointer was when the
/// saver started), suspend and resume, and a display change. Focus and activation changes never end it.
/// </summary>
internal static class SaverExitRules
{
    /// <summary>The movement allowed before the saver ends, in DIPs (5.4: 4 pixels at 96 DPI).</summary>
    public const int MovementThreshold = 4;

    /// <summary>True when the pointer moved far enough to end the saver.</summary>
    /// <param name="start">Where the pointer was when the saver started (physical pixels).</param>
    /// <param name="now">Where it is now (physical pixels).</param>
    /// <param name="scale">Physical pixels per DIP of the display under the pointer.</param>
    /// <returns>True when |dx| + |dy| exceeds 4 DIPs.</returns>
    public static bool MovedTooFar(PointI start, PointI now, double scale) =>
        Math.Abs((long)now.X - start.X) + Math.Abs((long)now.Y - start.Y) > MovementThreshold * scale;

    /// <summary>True for a window message that ends the saver regardless of the pointer position.</summary>
    /// <param name="message">The message.</param>
    /// <param name="wParam">Its wParam.</param>
    /// <param name="lParam">Its lParam.</param>
    /// <returns>True to end the saver.</returns>
    public static bool EndsSaver(uint message, nint wParam, nint lParam) => message switch
    {
        // Bit 30 is the previous key state: auto-repeat of a key held when the saver started does not count.
        SaverNativeMethods.WM_KEYDOWN or SaverNativeMethods.WM_SYSKEYDOWN => (lParam & (1 << 30)) == 0,
        SaverNativeMethods.WM_LBUTTONDOWN or SaverNativeMethods.WM_RBUTTONDOWN or SaverNativeMethods.WM_MBUTTONDOWN
            or SaverNativeMethods.WM_XBUTTONDOWN or SaverNativeMethods.WM_MOUSEWHEEL or SaverNativeMethods.WM_MOUSEHWHEEL
            or SaverNativeMethods.WM_POINTERDOWN or SaverNativeMethods.WM_DISPLAYCHANGE => true,
        SaverNativeMethods.WM_POWERBROADCAST => (int)wParam is SaverNativeMethods.PBT_APMSUSPEND
            or SaverNativeMethods.PBT_APMRESUMESUSPEND or SaverNativeMethods.PBT_APMRESUMEAUTOMATIC,
        _ => false,
    };
}

/// <summary>
/// Watches every key and mouse button through the asynchronous key state, so the saver also ends on a key press when one
/// of its windows could not take the keyboard focus. Keys held when the saver started count only after their release.
/// </summary>
internal sealed class AsyncKeyWatcher
{
    private const int FirstKey = 0x01;
    private const int LastKey = 0xFE;

    private readonly Func<int, bool> isDown;
    private readonly bool[] held = new bool[LastKey + 1];

    /// <summary>Takes the starting state of the real keyboard and mouse buttons.</summary>
    public AsyncKeyWatcher()
        : this(key => SaverNativeMethods.GetAsyncKeyState(key) < 0)
    {
    }

    /// <summary>Takes the starting state from a key-state function (tests).</summary>
    /// <param name="isDown">True when a virtual key is down.</param>
    internal AsyncKeyWatcher(Func<int, bool> isDown)
    {
        this.isDown = isDown;
        for (int key = FirstKey; key <= LastKey; key++)
        {
            held[key] = isDown(key);
        }
    }

    /// <summary>True when a key or button went down that was not already held.</summary>
    /// <returns>True for a new press.</returns>
    public bool NewPress()
    {
        bool pressed = false;
        for (int key = FirstKey; key <= LastKey; key++)
        {
            bool down = isDown(key);
            if (down && !held[key])
            {
                pressed = true;
            }

            held[key] = down;
        }

        return pressed;
    }
}
