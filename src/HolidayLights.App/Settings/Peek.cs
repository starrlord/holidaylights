using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace HolidayLights.App.Settings;

/// <summary>
/// Peek (PRODUCT-SPEC 3.2.3): hides the Settings window for a moment so the real desktop shows. Press and hold to peek
/// while held; a click shorter than 300 ms, Enter or Space peeks for 3 s, and any key ends a timed peek early. The window
/// is cloaked with <c>DWMWA_CLOAK</c> (instant, no opacity change, works with Mica) and uncloaked with focus restored.
/// </summary>
public sealed partial class Peek
{
    /// <summary>A press shorter than this is a click (a timed peek).</summary>
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(300);

    /// <summary>How long a timed peek lasts.</summary>
    public static readonly TimeSpan TimedDuration = TimeSpan.FromSeconds(3);

    private const int DwmwaCloak = 13;

    private readonly Window window;
    private readonly DispatcherTimer timer;
    private long pressedAt;
    private IInputElement? focusBefore;

    /// <summary>Creates the peek of a window.</summary>
    /// <param name="window">The Settings window.</param>
    public Peek(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        this.window = window;
        timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimedDuration };
        timer.Tick += (_, _) => End();
        window.PreviewKeyDown += (_, e) =>
        {
            if (IsPeeking && timer.IsEnabled)
            {
                End();
                e.Handled = true;
            }
        };
        window.Deactivated += (_, _) =>
        {
            if (IsPeeking && !timer.IsEnabled && Mouse.LeftButton != MouseButtonState.Pressed)
            {
                End();
            }
        };
    }

    /// <summary>True while the window is hidden.</summary>
    public bool IsPeeking { get; private set; }

    /// <summary>The peek button was pressed: hide at once.</summary>
    public void Press()
    {
        pressedAt = Stopwatch.GetTimestamp();
        Begin();
    }

    /// <summary>The peek button was released: a short press becomes a 3 s peek, a hold ends now.</summary>
    public void Release()
    {
        if (!IsPeeking)
        {
            return;
        }

        if (Stopwatch.GetElapsedTime(pressedAt) < HoldThreshold)
        {
            timer.Start();
        }
        else
        {
            End();
        }
    }

    /// <summary>Peeks for 3 s (keyboard).</summary>
    public void PeekTimed()
    {
        Begin();
        timer.Stop();
        timer.Start();
    }

    /// <summary>Shows the window again and gives focus back.</summary>
    public void End()
    {
        timer.Stop();
        if (!IsPeeking)
        {
            return;
        }

        IsPeeking = false;
        Cloak(false);
        window.Activate();
        if (focusBefore is UIElement { IsVisible: true } element)
        {
            element.Focus();
        }

        focusBefore = null;
    }

    private void Begin()
    {
        if (IsPeeking)
        {
            return;
        }

        focusBefore = Keyboard.FocusedElement;
        IsPeeking = true;
        Cloak(true);
    }

    private void Cloak(bool cloak)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int value = cloak ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaCloak, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
