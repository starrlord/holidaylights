using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.App.Styles;

namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>
/// Helpers for rendering real Settings windows in tests and the visual harness: the Fluent theme and the application
/// dictionaries on a window (as App.xaml would provide them), dispatcher pumping, and PNG output at 150 %.
/// </summary>
public static class UiHarness
{
    /// <summary>The environment variable that names a folder for the harness's PNG files.</summary>
    public const string SnapshotFolderVariable = "HL_UI_SNAPSHOTS";

    /// <summary>The DPI of the reference PC (150 %).</summary>
    public const double ReferenceDpi = 144;

    /// <summary>
    /// Gives a window the Fluent theme and the dictionaries App.xaml merges (tokens, illustrations, tooltips). Windows that
    /// declare their own resources do not receive the Fluent dictionary from <c>Window.ThemeMode</c>, so it is merged
    /// explicitly, as <c>Application.ThemeMode</c> does in the app.
    /// </summary>
    /// <param name="window">The window (not shown yet).</param>
    /// <param name="dark">Dark or light theme.</param>
    /// <param name="highContrast">True for the Fluent High Contrast dictionary and the app's High Contrast overrides (the
    /// system colors stay the machine's: an approximation of a real High Contrast theme).</param>
    public static void ApplyAppResources(Window window, bool dark, bool highContrast = false)
    {
        _ = Application.Current; // registers the pack:// scheme
        string variant = highContrast ? "HC" : dark ? "Dark" : "Light";
        string fluent = $"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{variant}.xaml";
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(fluent, UriKind.Absolute) });
        window.Resources.MergedDictionaries.Add(Load("/HolidayLights;component/Styles/Theme.xaml"));
        window.Resources.MergedDictionaries.Add(Load("/HolidayLights;component/Assets/Illustrations.xaml"));
        HighContrastResources.Attach(window.Resources);
        if (highContrast)
        {
            HighContrastResources.Apply(window.Resources, highContrast: true);
        }
    }

    /// <summary>Places a window far off-screen so showing it does not disturb the desktop.</summary>
    /// <param name="window">The window.</param>
    /// <param name="width">Width in DIP.</param>
    /// <param name="height">Height in DIP.</param>
    public static void PlaceOffScreen(Window window, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.Width = width;
        window.Height = height;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    /// <summary>Runs the dispatcher for a while (layout, timers, finished background work).</summary>
    /// <param name="duration">How long.</param>
    public static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Renders an element over the Mica base colour of the theme at 150 %.</summary>
    /// <param name="element">The element (laid out).</param>
    /// <param name="dark">Dark or light Mica base.</param>
    /// <returns>The bitmap.</returns>
    public static BitmapSource Render(FrameworkElement element, bool dark)
    {
        double scale = ReferenceDpi / 96;
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        var visual = new DrawingVisual();
        Vector offset = VisualTreeHelper.GetOffset(element);
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)), null, bounds);
            if (offset != default)
            {
                // An element placed with a margin is drawn through a brush whose view box starts at that offset.
                var viewbox = new Rect(offset.X, offset.Y, bounds.Width, bounds.Height);
                context.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = viewbox }, null, bounds);
            }
        }

        var target = new RenderTargetBitmap(width, height, ReferenceDpi, ReferenceDpi, PixelFormats.Pbgra32);
        target.Render(visual);
        if (offset == default)
        {
            target.Render(element);
        }

        target.Freeze();
        return target;
    }

    /// <summary>Writes a bitmap as PNG.</summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <param name="path">The file.</param>
    public static void SavePng(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Presses a key on an element (its PreviewKeyDown, then its KeyDown unless handled) and lets the window react.</summary>
    /// <param name="target">The element (usually the focused one).</param>
    /// <param name="key">The key.</param>
    public static void Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        }

        Pump(TimeSpan.FromMilliseconds(30));
    }

    /// <summary>Every element of a type in the visual tree below an element.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="root">The element.</param>
    /// <returns>The elements, depth first.</returns>
    public static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The roots of the popups this thread has open (menus, flyouts, drop-down lists), for rendering.</summary>
    /// <returns>The popup roots.</returns>
    public static IReadOnlyList<FrameworkElement> OpenPopups() =>
    [
        .. PresentationSource.CurrentSources.OfType<PresentationSource>()
            .Where(s => s.CheckAccess() && s.RootVisual is FrameworkElement { IsVisible: true } root && root.GetType().Name == "PopupRoot")
            .Select(s => (FrameworkElement)s.RootVisual),
    ];

    /// <summary>The snapshot folder, or null when the harness should not write files.</summary>
    public static string? SnapshotFolder => Environment.GetEnvironmentVariable(SnapshotFolderVariable) is { Length: > 0 } folder ? folder : null;

    private static ResourceDictionary Load(string uri) =>
        (ResourceDictionary)Application.LoadComponent(new Uri(uri, UriKind.Relative));
}
