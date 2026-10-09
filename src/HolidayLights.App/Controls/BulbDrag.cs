using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HolidayLights.App.Controls;

/// <summary>What is being dragged: bulbs from the Bulb List, or the bulb of a chip or a corner.</summary>
/// <param name="BulbIds">The bulbs (several from a multi-selection of the list).</param>
/// <param name="Source">The box the bulb comes from, or null for the Bulb List.</param>
public sealed record BulbDragData(IReadOnlyList<string> BulbIds, BoxPosition? Source)
{
    /// <summary>Set by a drop target of the frame editor that handled (or refused) the drop.</summary>
    public bool DroppedOnFrame { get; set; }

    /// <summary>True while the pointer is over the frame editor: releasing the button there never removes the dragged bulb.</summary>
    public bool OverFrame { get; set; }

    /// <summary>A sentence shown beside the pointer while a drop is refused ("This edge already has 6 bulb types. ..."), or null.</summary>
    public string? Hint { get; set; }
}

/// <summary>
/// Dragging bulbs (PRODUCT-SPEC 3.2.7): starts after the system drag distance, shows the bulb's list preview at 2x and
/// 85 % opaque beside the pointer ("+" while a chip is copied, "Remove from Top Edge" where a drop would remove it), and
/// tells the source whether the drag ended with a drop or Esc.
/// </summary>
public static partial class BulbDrag
{
    /// <summary>The data format of bulb drags inside Holiday Lights.</summary>
    public const string Format = "HolidayLights.BulbDrag";

    /// <summary>The bulbs of a drag, or null for other data (files).</summary>
    /// <param name="data">The drag data.</param>
    /// <returns>The bulb drag, or null.</returns>
    public static BulbDragData? From(IDataObject data) =>
        data?.GetDataPresent(Format) == true ? data.GetData(Format) as BulbDragData : null;

    /// <summary>True when a mouse movement since <paramref name="start"/> is far enough to start a drag.</summary>
    /// <param name="start">Where the button went down.</param>
    /// <param name="current">The pointer now.</param>
    /// <returns>True beyond the system drag distance.</returns>
    public static bool IsDragDistance(Point start, Point current) =>
        Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>Runs a drag (blocks until the drop or Esc, like every OLE drag).</summary>
    /// <param name="source">The element the drag starts from.</param>
    /// <param name="data">What is dragged.</param>
    /// <param name="services">The services (the drag picture).</param>
    /// <param name="removalText">The label where a drop would remove the bulb ("Remove from Top Edge"), or null for list drags.</param>
    /// <returns>True when the drag ended with a drop (not Esc) outside the frame editor.</returns>
    public static bool Run(DependencyObject source, BulbDragData data, IAppServices services, string? removalText)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(services);
        bool dropped = false;
        using var visual = new DragVisual(services, data, removalText);
        var dataObject = new DataObject(Format, data);

        void OnQueryContinue(object sender, QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed)
            {
                e.Action = DragAction.Cancel;
                e.Handled = true;
            }
            else if ((e.KeyStates & DragDropKeyStates.LeftMouseButton) == 0)
            {
                dropped = true;
            }
        }

        void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            visual.Update(e.Effects);
            e.UseDefaultCursors = true;
        }

        DragDrop.AddQueryContinueDragHandler(source, OnQueryContinue);
        DragDrop.AddGiveFeedbackHandler(source, OnGiveFeedback);
        try
        {
            visual.Update(DragDropEffects.None);
            DragDrop.DoDragDrop(source, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            DragDrop.RemoveQueryContinueDragHandler(source, OnQueryContinue);
            DragDrop.RemoveGiveFeedbackHandler(source, OnGiveFeedback);
        }

        return dropped && !data.DroppedOnFrame && !data.OverFrame;
    }

    /// <summary>The picture that follows the pointer: a small click-through, non-activating topmost window.</summary>
    private sealed partial class DragVisual : IDisposable
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20;
        private const int WsExLayered = 0x80000;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x80;

        private readonly Window window;
        private readonly Border copyBadge;
        private readonly Border label;
        private readonly TextBlock labelText;
        private readonly BulbDragData data;
        private readonly string? removalText;

        public DragVisual(IAppServices services, BulbDragData data, string? removalText)
        {
            this.data = data;
            this.removalText = removalText;
            var thumbnail = new BulbThumbnail { Services = services, BulbId = data.BulbIds.FirstOrDefault(), ShowListPreview = true, Width = 64, Height = 64 };
            copyBadge = Badge("+", 16);
            copyBadge.HorizontalAlignment = HorizontalAlignment.Right;
            copyBadge.VerticalAlignment = VerticalAlignment.Bottom;
            label = Badge("", 11);
            labelText = (TextBlock)label.Child;
            labelText.TextWrapping = TextWrapping.Wrap;
            label.MaxWidth = 240;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(76, 4, 0, 0);
            var count = data.BulbIds.Count > 1 ? Badge(data.BulbIds.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), 11) : null;
            var root = new Grid { Opacity = 0.85 };
            root.Children.Add(new NightWell { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, Child = thumbnail });
            root.Children.Add(copyBadge);
            root.Children.Add(label);
            if (count is not null)
            {
                count.HorizontalAlignment = HorizontalAlignment.Left;
                count.VerticalAlignment = VerticalAlignment.Top;
                root.Children.Add(count);
            }

            window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                IsHitTestVisible = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new Grid { Width = 320, Height = 80, Children = { root } },
                Left = -32000,
                Top = -32000,
            };
            window.SourceInitialized += (_, _) =>
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                _ = SetWindowLongW(handle, GwlExStyle, GetWindowLongW(handle, GwlExStyle) | WsExTransparent | WsExLayered | WsExNoActivate | WsExToolWindow);
            };
            window.Show();
        }

        public void Update(DragDropEffects effects)
        {
            bool fromBox = data.Source is not null;
            bool accepted = effects != DragDropEffects.None;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            copyBadge.Visibility = fromBox && accepted && !shift ? Visibility.Visible : Visibility.Collapsed;
            string? text = data.Hint ?? (fromBox && !accepted && !data.OverFrame ? removalText : null);
            labelText.Text = text ?? "";
            label.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            if (GetCursorPos(out NativePoint point) && PresentationSource.FromVisual(window) is { CompositionTarget: { } target })
            {
                Point dip = target.TransformFromDevice.Transform(new Point(point.X, point.Y));
                window.Left = dip.X + 12;
                window.Top = dip.Y + 12;
            }
        }

        public void Dispose() => window.Close();

        private static Border Badge(string text, double size) => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 2),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = size, FontWeight = FontWeights.SemiBold },
        };

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out NativePoint point);

        [LibraryImport("user32.dll")]
        private static partial int GetWindowLongW(IntPtr hwnd, int index);

        [LibraryImport("user32.dll")]
        private static partial int SetWindowLongW(IntPtr hwnd, int index, int value);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }
    }
}
