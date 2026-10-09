using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HolidayLights.App.Settings;

/// <summary>Where the Settings window opens: a display, a size in DIP, a top-left corner in physical pixels.</summary>
/// <param name="Display">The display it opens on.</param>
/// <param name="Size">The window size in DIP.</param>
/// <param name="TopLeft">The top-left corner in physical pixels of the virtual screen.</param>
/// <param name="Maximized">Open maximized.</param>
public sealed record WindowPlacementPlan(DisplayInfo Display, Size Size, PointI TopLeft, bool Maximized);

/// <summary>
/// Size and position of the Settings window (PRODUCT-SPEC 2.3): 1240 x 800 DIP by default, clamped to 92 % of the work area
/// of the display under the mouse pointer and at least 760 x 560; centred on that display the first time; later at the
/// remembered size, position and maximized state, re-clamped when the display layout changed.
/// </summary>
public static partial class WindowPlacement
{
    /// <summary>The default width.</summary>
    public const double DefaultWidth = 1240;

    /// <summary>The default height.</summary>
    public const double DefaultHeight = 800;

    /// <summary>The minimum width.</summary>
    public const double MinWidth = 760;

    /// <summary>The minimum height.</summary>
    public const double MinHeight = 560;

    /// <summary>The largest share of the work area the window takes.</summary>
    public const double MaxShare = 0.92;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Plans the placement.</summary>
    /// <param name="remembered">The remembered placement (DIP of the virtual screen at the main display's scale), or null.</param>
    /// <param name="displays">The displays.</param>
    /// <param name="pointerDisplay">The display under the mouse pointer (where a new window opens).</param>
    /// <returns>The plan.</returns>
    public static WindowPlacementPlan Plan(WindowPlacementSettings? remembered, IReadOnlyList<DisplayInfo> displays, DisplayInfo pointerDisplay)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(pointerDisplay);
        DisplayInfo primary = displays.FirstOrDefault(d => d.IsPrimary) ?? pointerDisplay;
        if (remembered is not null && FindHome(remembered, displays, primary.Scale) is { } home)
        {
            Size size = ClampSize(new Size(remembered.Width, remembered.Height), home);
            RectI work = home.WorkArea;
            int width = (int)Math.Round(size.Width * home.Scale);
            int height = (int)Math.Round(size.Height * home.Scale);
            int left = Math.Clamp((int)Math.Round(remembered.Left * primary.Scale), work.Left, Math.Max(work.Left, work.Right - width));
            int top = Math.Clamp((int)Math.Round(remembered.Top * primary.Scale), work.Top, Math.Max(work.Top, work.Bottom - height));
            return new WindowPlacementPlan(home, size, new PointI(left, top), remembered.Maximized);
        }

        Size fresh = ClampSize(new Size(DefaultWidth, DefaultHeight), pointerDisplay);
        return new WindowPlacementPlan(pointerDisplay, fresh, Centered(fresh, pointerDisplay), false);
    }

    /// <summary>Clamps a size to 92 % of a display's work area, but never below the minimum size.</summary>
    /// <param name="size">The wanted size in DIP.</param>
    /// <param name="display">The display.</param>
    /// <returns>The size in DIP.</returns>
    public static Size ClampSize(Size size, DisplayInfo display)
    {
        ArgumentNullException.ThrowIfNull(display);
        double maxWidth = display.WorkArea.Width / display.Scale * MaxShare;
        double maxHeight = display.WorkArea.Height / display.Scale * MaxShare;
        return new Size(
            Math.Round(Math.Max(MinWidth, Math.Min(size.Width, maxWidth))),
            Math.Round(Math.Max(MinHeight, Math.Min(size.Height, maxHeight))));
    }

    /// <summary>Records a window's size, position and state (from its restore bounds while maximized).</summary>
    /// <param name="window">The window.</param>
    /// <param name="primaryScale">The main display's scale (the DIP unit of the stored position).</param>
    /// <returns>The placement to remember.</returns>
    public static WindowPlacementSettings Capture(Window window, double primaryScale)
    {
        ArgumentNullException.ThrowIfNull(window);
        Rect bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
        double currentScale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? primaryScale;
        return new WindowPlacementSettings
        {
            Left = Math.Round(bounds.Left * currentScale / primaryScale),
            Top = Math.Round(bounds.Top * currentScale / primaryScale),
            Width = Math.Round(bounds.Width),
            Height = Math.Round(bounds.Height),
            Maximized = window.WindowState == WindowState.Maximized,
        };
    }

    /// <summary>Applies a plan before the window is shown: the DIP size now, the physical position once the window has a handle.</summary>
    /// <param name="window">The window (not shown yet).</param>
    /// <param name="plan">The plan.</param>
    /// <param name="primaryScale">The main display's scale.</param>
    public static void Apply(Window window, WindowPlacementPlan plan, double primaryScale)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(plan);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = plan.Size.Width;
        window.Height = plan.Size.Height;
        window.Left = plan.TopLeft.X / primaryScale;
        window.Top = plan.TopLeft.Y / primaryScale;
        window.SourceInitialized += OnSourceInitialized;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            IntPtr handle = new WindowInteropHelper(window).Handle;

            // Moving the corner onto the target display lets WPF adopt that display's DPI while keeping the DIP size.
            _ = SetWindowPos(handle, IntPtr.Zero, plan.TopLeft.X, plan.TopLeft.Y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
            if (plan.Maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
    }

    private static PointI Centered(Size size, DisplayInfo display)
    {
        RectI work = display.WorkArea;
        int width = (int)Math.Round(size.Width * display.Scale);
        int height = (int)Math.Round(size.Height * display.Scale);
        return new PointI(work.Left + Math.Max(0, (work.Width - width) / 2), work.Top + Math.Max(0, (work.Height - height) / 2));
    }

    /// <summary>The display that holds the centre of the remembered window, when at least half of its title area is visible.</summary>
    private static DisplayInfo? FindHome(WindowPlacementSettings remembered, IReadOnlyList<DisplayInfo> displays, double primaryScale)
    {
        if (remembered.Width < 1 || remembered.Height < 1)
        {
            return null;
        }

        var center = new PointI(
            (int)Math.Round((remembered.Left + remembered.Width / 2) * primaryScale),
            (int)Math.Round((remembered.Top + remembered.Height / 2) * primaryScale));
        var title = new PointI(center.X, (int)Math.Round((remembered.Top + 16) * primaryScale));
        return displays.FirstOrDefault(d => d.WorkArea.Contains(center) && d.WorkArea.Contains(title));
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
