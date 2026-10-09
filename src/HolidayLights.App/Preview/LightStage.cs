using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.App.Styles;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.Preview;

/// <summary>Where a click on a <see cref="LightStage"/> landed.</summary>
public sealed class LightStageHitEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="displayId">The display that was clicked.</param>
    /// <param name="placement">The bulb under the pointer, or null.</param>
    /// <param name="edge">The edge band under the pointer (also for a bulb on an edge), or null.</param>
    /// <param name="corner">The corner zone under the pointer (the outer 18 % at each corner), or null.</param>
    public LightStageHitEventArgs(string displayId, BulbPlacement? placement, Side? edge, Corner? corner)
    {
        DisplayId = displayId;
        Placement = placement;
        Edge = edge;
        Corner = corner;
    }

    /// <summary>The display that was clicked.</summary>
    public string DisplayId { get; }

    /// <summary>The bulb under the pointer, or null.</summary>
    public BulbPlacement? Placement { get; }

    /// <summary>The edge band under the pointer, or null.</summary>
    public Side? Edge { get; }

    /// <summary>The corner zone under the pointer, or null.</summary>
    public Corner? Corner { get; }
}

/// <summary>How a <see cref="LightStage"/> lays out its arrangement.</summary>
public enum StageGeometry
{
    /// <summary>Exactly like the desktop: the enabled displays' targets and the frame mode (Home, Bulb Factory, try-on).</summary>
    Desktop,

    /// <summary>The shown display alone, framed on its own (theme cards, the Welcome card, the screen saver).</summary>
    DisplayAlone,
}

/// <summary>
/// How much of the display a <see cref="LightStage"/> shows (PO decision 8: previews show real bulb art at a legible size).
/// Every mode keeps the exact layout and pattern; only the view changes.
/// </summary>
public enum StageZoom
{
    /// <summary>
    /// <see cref="Corner"/> for a display laid out alone (<see cref="StageGeometry.DisplayAlone"/>: theme cards); for
    /// Home's stage of every display, the whole desktop plus a magnified corner of each display when its bulbs would be
    /// tiny; otherwise <see cref="Fit"/>.
    /// </summary>
    Auto,

    /// <summary>The whole display (or every display) scaled uniformly to fit.</summary>
    Fit,

    /// <summary>
    /// A corner of the shown display (<see cref="LightStage.ZoomCorner"/>) at <see cref="LightStage.CornerScale"/> of its
    /// real size, cropped to the stage: the corner bulb and the start of both strips at a legible size. With every display
    /// shown, each display gets a magnified corner instead.
    /// </summary>
    Corner,
}

/// <summary>
/// The light stage (owner: settings-ui; PRODUCT-SPEC 3.0.2): the live, exact preview used by Home, Bulb Factory, theme
/// cards, the Welcome card and the screen saver page. It lays out in the display's physical pixels at its real scale and
/// bulb size with the real layout engine, scales the frame uniformly to fit (or zooms into a corner, <see cref="Zoom"/>),
/// draws the wallpaper backdrop, the taskbar band and the bezel, and composites bulbs (source-over) and glow (additive)
/// with the CPU compositor into a WriteableBitmap; it steps with the desktop clock and stops while invisible, minimized,
/// or while the lights rest because nobody can see the screen.
/// </summary>
/// <remarks>
/// <para>Services come from <see cref="Services"/> or, when unset, <see cref="AppServicesHost.Current"/>.</para>
/// <para>The backdrop and taskbar band are composed once per layout; each frame restores them, composites the bulbs and
/// uploads only the bands where bulbs and their glow are. A frame equal to the one on screen is not drawn again.</para>
/// </remarks>
public class LightStage : FrameworkElement
{
    /// <summary>Real pixels to stage pixels in <see cref="StageZoom.Corner"/> (a 48 px bulb shows at about 19 px).</summary>
    public const double CornerScale = 0.4;

    /// <summary>Identifies <see cref="Services"/>.</summary>
    public static readonly DependencyProperty ServicesProperty =
        DependencyProperty.Register(nameof(Services), typeof(IAppServices), typeof(LightStage), new PropertyMetadata(null, OnServicesChanged));

    /// <summary>Identifies <see cref="DisplayId"/>.</summary>
    public static readonly DependencyProperty DisplayIdProperty =
        DependencyProperty.Register(nameof(DisplayId), typeof(string), typeof(LightStage), new PropertyMetadata(null, OnInputChanged));

    /// <summary>Identifies <see cref="ShowAllDisplays"/>.</summary>
    public static readonly DependencyProperty ShowAllDisplaysProperty =
        DependencyProperty.Register(nameof(ShowAllDisplays), typeof(bool), typeof(LightStage), new PropertyMetadata(false, OnInputChanged));

    /// <summary>Identifies <see cref="Arrangement"/>.</summary>
    public static readonly DependencyProperty ArrangementProperty =
        DependencyProperty.Register(nameof(Arrangement), typeof(SlotAssignment), typeof(LightStage), new PropertyMetadata(null, OnInputChanged));

    /// <summary>Identifies <see cref="Flash"/>.</summary>
    public static readonly DependencyProperty FlashProperty =
        DependencyProperty.Register(nameof(Flash), typeof(FlashOptions), typeof(LightStage), new PropertyMetadata(null, OnInputChanged));

    /// <summary>Identifies <see cref="IsAnimated"/>.</summary>
    public static readonly DependencyProperty IsAnimatedProperty =
        DependencyProperty.Register(nameof(IsAnimated), typeof(bool), typeof(LightStage), new PropertyMetadata(true, OnAnimationChanged));

    /// <summary>Identifies <see cref="OverlayText"/>.</summary>
    public static readonly DependencyProperty OverlayTextProperty =
        DependencyProperty.Register(nameof(OverlayText), typeof(string), typeof(LightStage), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies <see cref="HighlightedSlot"/>.</summary>
    public static readonly DependencyProperty HighlightedSlotProperty =
        DependencyProperty.Register(nameof(HighlightedSlot), typeof(CellSlot?), typeof(LightStage), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies <see cref="Interval"/>.</summary>
    public static readonly DependencyProperty IntervalProperty =
        DependencyProperty.Register(nameof(Interval), typeof(int?), typeof(LightStage), new PropertyMetadata(null, OnInputChanged));

    /// <summary>Identifies <see cref="Geometry"/>.</summary>
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(nameof(Geometry), typeof(StageGeometry), typeof(LightStage), new PropertyMetadata(StageGeometry.Desktop, OnInputChanged));

    /// <summary>Identifies <see cref="Zoom"/>.</summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(StageZoom), typeof(LightStage), new PropertyMetadata(StageZoom.Auto, OnInputChanged));

    /// <summary>Identifies <see cref="ZoomCorner"/>.</summary>
    public static readonly DependencyProperty ZoomCornerProperty =
        DependencyProperty.Register(nameof(ZoomCorner), typeof(Corner), typeof(LightStage), new PropertyMetadata(Corner.TopRight, OnInputChanged));

    /// <summary>Identifies <see cref="ShowStatusOverlays"/>.</summary>
    public static readonly DependencyProperty ShowStatusOverlaysProperty =
        DependencyProperty.Register(nameof(ShowStatusOverlays), typeof(bool), typeof(LightStage), new PropertyMetadata(false, OnInputChanged));

    /// <summary>Identifies <see cref="EmptyText"/>.</summary>
    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(LightStage), new FrameworkPropertyMetadata("No bulbs yet.", FrameworkPropertyMetadataOptions.AffectsRender));

    private const string LogSource = "Settings.Preview";
    private const string SecondaryTextKey = "TextFillColorSecondaryBrush";
    private const string AccentKey = "AccentFillColorDefaultBrush";
    private const string PillKey = ThemeKeys.StagePillBrush;
    private const double BezelRadius = 6;
    private const double CornerZoneFraction = 0.18;

    /// <summary>Below this fit zoom Home's stage magnifies a corner of each display (a 48 px bulb would be under 12 px).</summary>
    private const double LensThreshold = 0.25;

    /// <summary>The magnified corner covers this share of the display's picture.</summary>
    private const double LensFraction = 0.5;

    /// <summary>Glow reaches at most this many times the largest bulb side beyond a strip (sigma 0.22 x the side, 3 sigma).</summary>
    private const double GlowReach = 1.5;

    /// <summary>The most backdrops a stage keeps (its current views, and the previous ones for a quick return).</summary>
    private const int BackdropCapacity = 8;

    private readonly DispatcherTimer frameTimer;
    private readonly DispatcherTimer backdropTimer;
    private readonly Border lightsOffPanel;
    private readonly Dictionary<(string DisplayId, SizeI Size), PremultipliedImage?> backdrops = [];
    private readonly List<Lens> lenses = [];
    private IAppServices? subscribed;
    private Window? window;
    private PreviewPlayer? player;
    private LightsScene? previewScene;
    private StageMap? map;
    private View? main;
    private BulbVisualState[]? staticStates;
    private StepClock? ownClock;
    private IReadOnlyList<DisplayScene> shownDisplays = [];
    private int backdropGeneration;
    private bool resting;

    static LightStage()
    {
        FocusableProperty.OverrideMetadata(typeof(LightStage), new FrameworkPropertyMetadata(false));
        ClipToBoundsProperty.OverrideMetadata(typeof(LightStage), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates the stage.</summary>
    public LightStage()
    {
        frameTimer = new DispatcherTimer(DispatcherPriority.Render);
        frameTimer.Tick += (_, _) => RenderFrame();
        backdropTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        backdropTimer.Tick += (_, _) =>
        {
            backdropTimer.Stop();
            RequestBackdrops();
        };
        lightsOffPanel = CreateLightsOffPanel();
        AddVisualChild(lightsOffPanel);
        AddLogicalChild(lightsOffPanel);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    /// <summary>Raised when the stage is clicked (Bulb Factory: click to select; Home: open Bulb Factory on that display).</summary>
    public event EventHandler<LightStageHitEventArgs>? Hit;

    /// <summary>Raised when <see cref="ScreenArea"/> changes.</summary>
    public event EventHandler? ScreenAreaChanged;

    /// <summary>Where the displays are drawn, in DIP relative to the stage (letterboxed), or <see cref="Rect.Empty"/> when none are.</summary>
    public Rect ScreenArea { get; private set; } = Rect.Empty;

    /// <summary>The services, or null to use <see cref="AppServicesHost.Current"/>.</summary>
    public IAppServices? Services
    {
        get => (IAppServices?)GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    /// <summary>The display to show (a <see cref="DisplayInfo.DeviceId"/>); null = the main display.</summary>
    public string? DisplayId
    {
        get => (string?)GetValue(DisplayIdProperty);
        set => SetValue(DisplayIdProperty, value);
    }

    /// <summary>Home: every display at its real position and relative size; disabled displays as outlines captioned "No lights".</summary>
    public bool ShowAllDisplays
    {
        get => (bool)GetValue(ShowAllDisplaysProperty);
        set => SetValue(ShowAllDisplaysProperty, value);
    }

    /// <summary>The arrangement to show (theme cards, try-on), or null for the desktop's.</summary>
    public SlotAssignment? Arrangement
    {
        get => (SlotAssignment?)GetValue(ArrangementProperty);
        set => SetValue(ArrangementProperty, value);
    }

    /// <summary>The flash options to show (a theme's own pattern), or null for the desktop's effective options.</summary>
    public FlashOptions? Flash
    {
        get => (FlashOptions?)GetValue(FlashProperty);
        set => SetValue(FlashProperty, value);
    }

    /// <summary>Animate with the pattern; false shows frame 0, lit (static theme cards).</summary>
    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <summary>The pill at the top-left ("Trying On: Candy Canes - the whole frame", "Preview: Thanksgiving"), or null.</summary>
    public string? OverlayText
    {
        get => (string?)GetValue(OverlayTextProperty);
        set => SetValue(OverlayTextProperty, value);
    }

    /// <summary>The box whose strip or corner is outlined with the accent colour, or null.</summary>
    public CellSlot? HighlightedSlot
    {
        get => (CellSlot?)GetValue(HighlightedSlotProperty);
        set => SetValue(HighlightedSlotProperty, value);
    }

    /// <summary>A flash interval of the stage's own (a theme's), or null to follow the desktop clock.</summary>
    public int? Interval
    {
        get => (int?)GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    /// <summary>How the arrangement is laid out (like the desktop, or on the shown display alone).</summary>
    public StageGeometry Geometry
    {
        get => (StageGeometry)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    /// <summary>How much of the display is shown: all of it, or a corner at a legible size (theme cards, try-on).</summary>
    public StageZoom Zoom
    {
        get => (StageZoom)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// The corner shown by <see cref="StageZoom.Corner"/>: top-right by default (theme cards keep their badges at the
    /// top-left, over the start of the top edge); the try-on shows its target's corner.
    /// </summary>
    public Corner ZoomCorner
    {
        get => (Corner)GetValue(ZoomCornerProperty);
        set => SetValue(ZoomCornerProperty, value);
    }

    /// <summary>Shows the desktop's state: "The lights are turned off." with "Turn On the Lights", "Not shown on this display", "Preview unavailable - reconnecting".</summary>
    public bool ShowStatusOverlays
    {
        get => (bool)GetValue(ShowStatusOverlaysProperty);
        set => SetValue(ShowStatusOverlaysProperty, value);
    }

    /// <summary>The text shown when the arrangement is empty.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>The layout on display, in physical pixels (for tests and the frame editor).</summary>
    public LightsLayout? CurrentLayout => player?.Layout;

    /// <summary>Stage pixels per physical pixel of the main view (for tests).</summary>
    public double CurrentZoom => map?.Zoom ?? 0;

    /// <summary>The physical area the main view shows (a corner in <see cref="StageZoom.Corner"/>), for tests.</summary>
    public RectI ShownRegion => map?.Region ?? default;

    /// <summary>The magnified corners drawn over Home's displays: display, zoom and where (DIP), for tests.</summary>
    public IReadOnlyList<(string DisplayId, double Zoom, Rect Area)> Lenses =>
        [.. lenses.Select(l => (l.Display.Display.DeviceId, l.View.Map.Zoom, l.View.Map.ContentRect))];

    /// <summary>How many frames were composited since the stage was created (for tests).</summary>
    internal int CompositedFrames { get; private set; }

    /// <summary>True while the frame timer is armed (for tests).</summary>
    internal bool IsAnimating => frameTimer.IsEnabled;

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override IEnumerator LogicalChildren => new object[] { lightsOffPanel }.GetEnumerator();

    /// <summary>The services the stage uses, or null when none are available.</summary>
    protected IAppServices? EffectiveServices => Services ?? (AppServicesHost.IsAvailable ? AppServicesHost.Current : null);

    /// <summary>Where a dragged bulb dropped on the stage goes (PRODUCT-SPEC 3.2.3): a corner zone (the outer 18 % of the width and height at each corner) replaces that corner; elsewhere the nearest edge appends it.</summary>
    /// <param name="point">The point in this element's coordinates.</param>
    /// <returns>The side or the corner, or null outside every display.</returns>
    public (Side? Side, Corner? Corner)? GetDropTarget(Point point)
    {
        if (map is null || ToPhysical(point) is not { } physical || FindDisplay(physical) is not { } display)
        {
            return null;
        }

        RectI work = display.Display.WorkArea;
        double fx = (physical.X - work.Left) / (double)Math.Max(1, work.Width);
        double fy = (physical.Y - work.Top) / (double)Math.Max(1, work.Height);
        bool left = fx < CornerZoneFraction;
        bool right = fx > 1 - CornerZoneFraction;
        bool top = fy < CornerZoneFraction;
        bool bottom = fy > 1 - CornerZoneFraction;
        if ((left || right) && (top || bottom))
        {
            return (null, top ? (left ? Corner.TopLeft : Corner.TopRight) : (left ? Corner.BottomLeft : Corner.BottomRight));
        }

        double toTop = fy;
        double toBottom = 1 - fy;
        double toLeft = fx * work.Width / Math.Max(1.0, work.Height);
        double toRight = (1 - fx) * work.Width / Math.Max(1.0, work.Height);
        double nearest = Math.Min(Math.Min(toTop, toBottom), Math.Min(toLeft, toRight));
        Side side = nearest == toTop ? Side.Top : nearest == toBottom ? Side.Bottom : nearest == toLeft ? Side.Left : Side.Right;
        return (side, null);
    }

    /// <summary>Renders the current picture again (after an external change that the stage cannot observe).</summary>
    public void Refresh() => Rebuild();

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index) =>
        index == 0 ? lightsOffPanel : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        lightsOffPanel.Measure(availableSize);
        double aspect = DesktopAspect();
        double width = double.IsInfinity(availableSize.Width) ? (double.IsInfinity(availableSize.Height) ? 320 : availableSize.Height * aspect) : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? width / aspect : availableSize.Height;
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size panel = lightsOffPanel.DesiredSize;
        lightsOffPanel.Arrange(new Rect((finalSize.Width - panel.Width) / 2, (finalSize.Height - panel.Height) / 2, panel.Width, panel.Height));
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (map is null || main?.Bitmap is not { } bitmap)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bezel = new Pen(TryBrush(AppResourceKeys.StageBezelBrush) ?? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), 1);
        bezel.Freeze();

        var shown = new GeometryGroup();
        foreach (DisplayScene display in shownDisplays)
        {
            Rect rect = map.ToDip(display.Display.Bounds);
            if (display.Enabled || !ShowAllDisplays)
            {
                shown.Children.Add(new RectangleGeometry(rect, BezelRadius, BezelRadius));
            }
        }

        // A zoomed corner shows only part of the display: everything stays inside the stage's picture area (and its bezel).
        Rect picture = map.ContentRect;
        picture.Inflate(1, 1);
        drawingContext.PushClip(new RectangleGeometry(picture));
        drawingContext.PushClip(shown);
        drawingContext.DrawImage(bitmap, map.ContentRect);
        drawingContext.Pop();

        foreach (DisplayScene display in shownDisplays)
        {
            Rect rect = map.ToDip(display.Display.Bounds);
            if (ShowAllDisplays && !display.Enabled)
            {
                var dashed = new Pen(TryBrush(SecondaryTextKey) ?? Brushes.Gray, 1) { DashStyle = DashStyles.Dash };
                drawingContext.DrawRoundedRectangle(null, dashed, rect, BezelRadius, BezelRadius);
                DrawCentered(drawingContext, "No lights", rect, TryBrush(SecondaryTextKey) ?? Brushes.Gray, 12, pixelsPerDip);
                continue;
            }

            drawingContext.DrawRoundedRectangle(null, bezel, rect, BezelRadius, BezelRadius);
        }

        DrawLenses(drawingContext, bezel);
        foreach (DisplayScene display in shownDisplays)
        {
            if (ShowAllDisplays && display.Enabled && shownDisplays.Count > 1)
            {
                Rect rect = map.ToDip(display.Display.Bounds);
                string caption = display.Display.IsPrimary ? $"Display {display.Display.Number} (Main)" : $"Display {display.Display.Number}";
                DrawLabel(drawingContext, caption, new Point(rect.Left + 8, rect.Bottom - 8 - 20), pixelsPerDip, alignBottom: false);
            }
        }

        DrawHighlight(drawingContext);
        drawingContext.Pop();
        DrawStatusText(drawingContext, pixelsPerDip);
        if (!string.IsNullOrEmpty(OverlayText) && shownDisplays.Count > 0)
        {
            // Top-left; a zoomed corner moves the pill to the far side, over the display's middle rather than its bulbs
            // (the bottom-left corner keeps it at the top-left: the display picker sits at the top-right).
            Rect first = VisibleDip(shownDisplays[0].Display);
            (bool right, bool bottom) = map.ZoomedCorner switch
            {
                Corner.TopLeft => (true, true),
                Corner.TopRight => (false, true),
                Corner.BottomRight => (false, false),
                _ => (false, false),
            };
            var anchor = new Point(right ? first.Right - 8 : first.Left + 8, bottom ? first.Bottom - 8 : first.Top + 8);
            DrawLabel(drawingContext, OverlayText, anchor, pixelsPerDip, alignBottom: bottom, alignRight: right);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Focusable)
        {
            Focus();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (HitTestClick(e.GetPosition(this)) is { } hit)
        {
            Hit?.Invoke(this, hit);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space && shownDisplays.FirstOrDefault() is { } first)
        {
            Hit?.Invoke(this, new LightStageHitEventArgs(first.Display.DeviceId, null, null, null));
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new LightStageAutomationPeer(this);

    private static void OnServicesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var stage = (LightStage)d;
        if (stage.IsLoaded)
        {
            stage.Detach();
            stage.Attach();
        }
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LightStage)d).Rebuild();

    private static void OnAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var stage = (LightStage)d;
        stage.ownClock = null;
        stage.UpdateAnimation();
        stage.RenderFrame();
    }

    private static Border CreateLightsOffPanel()
    {
        var text = new TextBlock { Text = "The lights are turned off.", FontSize = 14, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.OnNightWellBrush);
        var button = new Button { Content = "Turn On the Lights", HorizontalAlignment = HorizontalAlignment.Center };
        button.SetResourceReference(StyleProperty, "AccentButtonStyle");
        var panel = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Child = new StackPanel { Children = { text, button } },
            Visibility = Visibility.Collapsed,
        };
        panel.SetResourceReference(Border.BackgroundProperty, PillKey);
        button.Click += (_, _) =>
        {
            if (panel.Parent is LightStage { EffectiveServices: { } services })
            {
                services.Lights.SetLightsOn(true);
            }
        };
        return panel;
    }

    private Brush? TryBrush(string key) => TryFindResource(key) as Brush;

    private void Attach()
    {
        IAppServices? services = EffectiveServices;
        if (services is null || ReferenceEquals(services, subscribed))
        {
            Rebuild();
            return;
        }

        services.Lights.SceneChanged += OnSceneChanged;
        services.Lights.StatusChanged += OnStatusChanged;
        services.Lights.Clock.ClockChanged += OnClockChanged;
        services.Bulbs.Changed += OnCatalogChanged;
        subscribed = services;
        resting = PreviewActivity.IsResting(services);
        window = Window.GetWindow(this);
        if (window is not null)
        {
            window.StateChanged += OnWindowStateChanged;
        }

        Rebuild();
    }

    private void Detach()
    {
        frameTimer.Stop();
        backdropTimer.Stop();
        if (subscribed is not null)
        {
            subscribed.Lights.SceneChanged -= OnSceneChanged;
            subscribed.Lights.StatusChanged -= OnStatusChanged;
            subscribed.Lights.Clock.ClockChanged -= OnClockChanged;
            subscribed.Bulbs.Changed -= OnCatalogChanged;
            subscribed = null;
        }

        if (window is not null)
        {
            window.StateChanged -= OnWindowStateChanged;
            window = null;
        }

        player?.Dispose();
        player = null;
    }

    private void OnSceneChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>Health and rest changes: redraw the overlays; stop while the lights rest, and show the current step again on resume.</summary>
    private void OnStatusChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
        bool now = EffectiveServices is { } services && PreviewActivity.IsResting(services);
        if (now == resting)
        {
            return;
        }

        resting = now;
        UpdateAnimation();
        if (!resting)
        {
            RenderFrame();
        }
    }

    private void OnClockChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(UpdateAnimation);

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateAnimation();

    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e)
    {
        if (e.Change is BulbCatalogChange.Updated or BulbCatalogChange.Added or BulbCatalogChange.Removed
            && player?.Layout.Placements.Any(p => e.BulbIds.Contains(p.BulbId, BulbIds.Comparer)) != false)
        {
            Dispatcher.InvokeAsync(Rebuild);
        }
    }

    /// <summary>Recomputes what is shown: displays, layout, pattern, views, then draws.</summary>
    private void Rebuild()
    {
        if (!IsLoaded || EffectiveServices is not { } services)
        {
            return;
        }

        LightsScene scene = services.Lights.Scene;
        AppSettings settings = services.Settings.Current;
        shownDisplays = DisplaysToShow(scene, services);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        map = CreateMap(pixelsPerDip);
        Rect area = map?.ContentRect ?? Rect.Empty;
        if (area != ScreenArea)
        {
            ScreenArea = area;
            ScreenAreaChanged?.Invoke(this, EventArgs.Empty);
        }

        lenses.Clear();
        if (map is null)
        {
            main = null;
            InvalidateVisual();
            return;
        }

        LightsLayout layout;
        try
        {
            layout = BuildLayout(services, scene, settings);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            services.Log.Warn(LogSource, "A preview could not be laid out.", exception);
            layout = LightsLayout.Empty;
        }

        FlashOptions options = Flash ?? scene.Flash;
        if (player is null || !ReferenceEquals(player.Layout, layout) || player.Options != options)
        {
            player?.Dispose();
            player = new PreviewPlayer(services.Flash, services.Bulbs, layout, options, options.Pattern == FlashPatternId.DanceToMusic ? services.Music.Events : null);
            staticStates = ScenePreviewRenderer.CreateStaticStates(layout, services.Bulbs);
            ownClock = null;
        }

        IReadOnlyList<DisplayScene> drawn = [.. shownDisplays.Where(d => d.Enabled || Geometry == StageGeometry.DisplayAlone || !ShowAllDisplays)];
        previewScene = scene with { Displays = drawn, Layout = layout, LightsOn = !ShowStatusOverlays || scene.LightsOn };
        lightsOffPanel.Visibility = ShowStatusOverlays && !scene.LightsOn ? Visibility.Visible : Visibility.Collapsed;
        main = new View(map, drawn, layout);
        if (ShowAllDisplays && Zoom != StageZoom.Fit && map.Zoom < LensThreshold)
        {
            foreach (DisplayScene display in drawn.Where(d => d.Enabled))
            {
                Rect picture = map.ToDip(display.Display.Bounds);
                var where = new Rect(
                    picture.Left,
                    picture.Top,
                    Math.Round(picture.Width * LensFraction * pixelsPerDip) / pixelsPerDip,
                    Math.Round(picture.Height * LensFraction * pixelsPerDip) / pixelsPerDip);
                double zoom = Math.Max(CornerScale, map.Zoom * 2);
                lenses.Add(new Lens(display, new View(StageMap.CreateLens(display.Display.Bounds, zoom, where, pixelsPerDip), [display], layout)));
            }
        }

        backdropTimer.Stop();
        backdropTimer.Start();
        UpdateAnimation();
        RenderFrame();
    }

    /// <summary>The main view: every display fitted, or one display's corner zoomed.</summary>
    private StageMap? CreateMap(double pixelsPerDip)
    {
        if (shownDisplays.Count == 0)
        {
            return null;
        }

        bool corner = Zoom switch
        {
            StageZoom.Auto => Geometry == StageGeometry.DisplayAlone && !ShowAllDisplays,
            StageZoom.Corner => !ShowAllDisplays,
            _ => false,
        };
        return corner && shownDisplays.Count == 1
            ? StageMap.CreateCorner(shownDisplays[0].Display.Bounds, ZoomCorner, CornerScale, RenderSize, pixelsPerDip)
            : StageMap.Create(shownDisplays.Select(d => d.Display.Bounds), RenderSize, pixelsPerDip);
    }

    private IReadOnlyList<DisplayScene> DisplaysToShow(LightsScene scene, IAppServices services)
    {
        IReadOnlyList<DisplayScene> displays = scene.Displays.Count > 0
            ? scene.Displays
            : [.. services.Displays.Displays.Select(d => new DisplayScene(d, true))];
        if (ShowAllDisplays)
        {
            return displays;
        }

        DisplayScene? chosen = displays.FirstOrDefault(d => string.Equals(d.Display.DeviceId, DisplayId, StringComparison.Ordinal))
            ?? displays.FirstOrDefault(d => d.Display.IsPrimary)
            ?? displays.FirstOrDefault();
        return chosen is null ? [] : [chosen];
    }

    private LightsLayout BuildLayout(IAppServices services, LightsScene scene, AppSettings settings)
    {
        if (Geometry == StageGeometry.DisplayAlone)
        {
            DisplayInfo display = shownDisplays[0].Display;
            return PreviewLayouts.ForDisplay(services.Layout, services.Bulbs, display, settings.Lights.Size, Arrangement ?? settings.Current.Arrangement);
        }

        LightsLayout desktop = PreviewLayouts.ForDesktop(services.Layout, services.Bulbs, scene, settings, Arrangement);
        bool shownIsFramed = shownDisplays.Any(s => desktop.Displays.Any(d => d.Target.DisplayId == s.Display.DeviceId));
        return shownIsFramed || ShowAllDisplays
            ? desktop
            : PreviewLayouts.ForDisplay(services.Layout, services.Bulbs, shownDisplays[0].Display, settings.Lights.Size, Arrangement ?? settings.Current.Arrangement);
    }

    private StepClock CurrentClock(IAppServices services)
    {
        if (Interval is { } interval)
        {
            return ownClock ??= StepClock.Start(Stopwatch.GetTimestamp(), Math.Clamp(interval, FlashSettings.MinInterval, FlashSettings.MaxInterval));
        }

        return services.Lights.Clock.Clock;
    }

    private bool ShouldAnimate() =>
        IsAnimated && IsVisible && player is { IsStatic: false } && !PreviewActivity.IsMinimized(window)
        && EffectiveServices is { } services && !PreviewActivity.IsResting(services);

    private void UpdateAnimation()
    {
        frameTimer.Stop();
        if (ShouldAnimate() && EffectiveServices is { } services && player is not null)
        {
            ScheduleNext(services);
        }
    }

    private void ScheduleNext(IAppServices services)
    {
        long now = Stopwatch.GetTimestamp();
        long next = player!.NextRedraw(now, CurrentClock(services));
        if (next == long.MaxValue)
        {
            return;
        }

        double milliseconds = (next - now) * 1000.0 / Stopwatch.Frequency;
        frameTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 1, 1000));
        frameTimer.Start();
    }

    /// <summary>
    /// Composites one frame of every view (bulbs and glow over the composed backdrop) and uploads what changed. Nothing is
    /// drawn while the lights rest because nobody can see the screen; the end of the rest draws the current frame.
    /// </summary>
    private void RenderFrame()
    {
        frameTimer.Stop();
        if (main is null || previewScene is null || player is null || resting || EffectiveServices is not { } services)
        {
            return;
        }

        bool animate = ShouldAnimate();
        IReadOnlyList<BulbVisualState> states = animate
            ? player.Sample(Stopwatch.GetTimestamp(), CurrentClock(services))
            : staticStates ?? ScenePreviewRenderer.CreateStaticStates(player.Layout, services.Bulbs);
        float glow = SystemParameters.HighContrast ? 0f : GlowLevels.Intensity(previewScene.Effects.Glow);
        bool dim = ShowStatusOverlays && !services.Lights.Scene.LightsOn;
        ulong signature = FrameSignature.Of(states, dim ? -glow - 1 : glow);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        bool changed = false;
        foreach (View view in Views())
        {
            changed |= Composite(view, services, states, glow, dim, signature, dpi);
        }

        if (changed)
        {
            InvalidateVisual();
        }

        if (animate)
        {
            ScheduleNext(services);
        }
    }

    /// <summary>Draws a view's frame unless it already shows it; returns true when its bitmap changed.</summary>
    private bool Composite(View view, IAppServices services, IReadOnlyList<BulbVisualState> states, float glow, bool dim, ulong signature, DpiScale dpi)
    {
        if (!view.BaseDirty && view.Bitmap is not null && view.Signature == signature)
        {
            return false;
        }

        StageMap viewMap = view.Map;
        if (view.Base is null || view.BaseDirty)
        {
            view.Base = ComposeBase(view, services);
            view.BaseDirty = false;
            view.FullUpload = true;
        }

        view.Buffer ??= new PremultipliedImage(viewMap.PixelSize.Width, viewMap.PixelSize.Height);
        Array.Copy(view.Base.Pixels, view.Buffer.Pixels, view.Base.Pixels.Length);
        try
        {
            if (previewScene!.LightsOn)
            {
                foreach (DisplayScene display in view.Displays)
                {
                    services.Compositor.DrawLights(view.Buffer, new LightsRenderRequest
                    {
                        Layout = view.Layout,
                        DisplayId = display.Display.DeviceId,
                        States = states,
                        Bulbs = services.Bulbs,
                        Sprites = services.Sprites,
                        Zoom = viewMap.Zoom,
                        OffsetX = viewMap.OffsetX,
                        OffsetY = viewMap.OffsetY,
                        Style = previewScene.Effects.Pixels,
                        GlowIntensity = glow,
                    });
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            services.Log.Warn(LogSource, "A preview frame could not be drawn.", exception);
        }

        if (dim)
        {
            PixelBuffers.Dim(view.Buffer, new RectI(0, 0, view.Buffer.Width, view.Buffer.Height), 0.5f);
            view.FullUpload = true;
        }

        CompositedFrames++;
        if (view.FullUpload || view.Bitmap is null || view.DirtyRects.Count == 0)
        {
            view.Bitmap = PixelBuffers.Present(view.Bitmap, view.Buffer, dpi);
            view.FullUpload = dim;
        }
        else
        {
            PixelBuffers.PresentAreas(view.Bitmap, view.Buffer, view.DirtyRects);
        }

        view.Signature = signature;
        return true;
    }

    /// <summary>The backdrop of each display (wallpaper, else the night gradient) and the taskbar bands, without bulbs.</summary>
    private PremultipliedImage ComposeBase(View view, IAppServices services)
    {
        StageMap viewMap = view.Map;
        var target = new PremultipliedImage(viewMap.PixelSize.Width, viewMap.PixelSize.Height);
        view.MissingBackdrop = false;
        foreach (DisplayScene display in view.Displays)
        {
            RectI area = viewMap.ToPixels(display.Display.Bounds);
            if (!backdrops.TryGetValue((display.Display.DeviceId, area.Size), out PremultipliedImage? backdrop))
            {
                view.MissingBackdrop = true;
            }

            if (backdrop is not null)
            {
                PixelBuffers.Blit(target, backdrop.Pixels, backdrop.Width, backdrop.Height, area.Left, area.Top, area);
            }
            else
            {
                PixelBuffers.FillNightGradient(target, area);
            }
        }

        try
        {
            new ScenePreviewRenderer(services.Sprites, services.Compositor).RenderInto(target, new ScenePreviewRequest
            {
                Scene = previewScene! with { Displays = view.Displays, LightsOn = false },
                Bulbs = services.Bulbs,
                States = [],
                Zoom = viewMap.Zoom,
                Backdrop = PreviewBackdrop.None,
            }, null, viewMap.OffsetX, viewMap.OffsetY);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            services.Log.Warn(LogSource, "A preview backdrop could not be drawn.", exception);
        }

        return target;
    }

    private async void RequestBackdrops()
    {
        if (main is null || previewScene is null || EffectiveServices is not { } services)
        {
            return;
        }

        int generation = ++backdropGeneration;
        RectI desktop = default;
        foreach (DisplayInfo display in services.Displays.Displays)
        {
            desktop = desktop.Union(display.Bounds);
        }

        var wanted = new Dictionary<(string, SizeI), DisplayInfo>();
        foreach (View view in Views())
        {
            foreach (DisplayScene display in view.Displays)
            {
                wanted.TryAdd((display.Display.DeviceId, view.Map.ToPixels(display.Display.Bounds).Size), display.Display);
            }
        }

        foreach (((string DisplayId, SizeI Size) key, DisplayInfo display) in wanted)
        {
            if (backdrops.ContainsKey(key))
            {
                continue;
            }

            PremultipliedImage? image = await WallpaperBackdrops.Shared.GetAsync(services.Wallpapers, display, desktop, key.Size, services.Log);
            if (generation != backdropGeneration)
            {
                return;
            }

            backdrops[key] = image;
        }

        if (backdrops.Count > BackdropCapacity)
        {
            foreach ((string, SizeI) key in backdrops.Keys.Where(k => !wanted.ContainsKey(k)).ToArray())
            {
                backdrops.Remove(key);
            }
        }

        bool any = false;
        foreach (View view in Views().Where(v => v.MissingBackdrop))
        {
            view.BaseDirty = true;
            any = true;
        }

        if (any)
        {
            RenderFrame();
        }
    }

    /// <summary>The views drawn now: the main view and the magnified corners.</summary>
    private IEnumerable<View> Views() => main is null ? [] : lenses.Select(l => l.View).Prepend(main);

    private Point? ToPhysicalPoint(Point point) => map?.ToPhysical(point);

    private PointI? ToPhysical(Point point) => ToPhysicalPoint(point) is { } p ? new PointI((int)Math.Floor(p.X), (int)Math.Floor(p.Y)) : null;

    private DisplayScene? FindDisplay(PointI physical) =>
        shownDisplays.FirstOrDefault(d => d.Display.Bounds.Contains(physical));

    /// <summary>The part of a display's picture inside the stage's picture area (all of it unless a corner is zoomed).</summary>
    private Rect VisibleDip(DisplayInfo display)
    {
        Rect rect = map!.ToDip(display.Bounds);
        rect.Intersect(map.ContentRect);
        return rect.IsEmpty ? map.ContentRect : rect;
    }

    /// <summary>Maps a click to a bulb, an edge band, a corner zone or the middle of a display.</summary>
    private LightStageHitEventArgs? HitTestClick(Point point)
    {
        if (ToPhysical(point) is not { } physical || FindDisplay(physical) is not { } display || player is null)
        {
            return null;
        }

        string id = display.Display.DeviceId;
        BulbPlacement? placement = player.Layout.Placements.FirstOrDefault(p => p.DisplayId == id && p.Bounds.Contains(physical));
        if (placement is not null)
        {
            StripLayout strip = StripOf(placement);
            return placement.IsCorner
                ? new LightStageHitEventArgs(id, placement, null, placement.Slot.ToCorner())
                : new LightStageHitEventArgs(id, placement, strip.Side, null);
        }

        RectI work = display.Display.WorkArea;
        int band = BandThickness(id, work);
        bool left = physical.X < work.Left + band;
        bool right = physical.X >= work.Right - band;
        bool top = physical.Y < work.Top + band;
        bool bottom = physical.Y >= work.Bottom - band;
        if ((left || right) && (top || bottom))
        {
            Corner corner = top ? (left ? Corner.TopLeft : Corner.TopRight) : (left ? Corner.BottomLeft : Corner.BottomRight);
            return new LightStageHitEventArgs(id, null, null, corner);
        }

        Side? edge = top ? Side.Top : bottom ? Side.Bottom : left ? Side.Left : right ? Side.Right : null;
        return new LightStageHitEventArgs(id, null, edge, null);
    }

    private StripLayout StripOf(BulbPlacement placement) =>
        player!.Layout.Displays.SelectMany(d => d.Strips).First(s => s.DisplayId == placement.DisplayId && s.Index == placement.StripIndex);

    /// <summary>The thickness of the clickable edge bands: the thickest strip, at least 6 % of the shorter side.</summary>
    private int BandThickness(string displayId, RectI work)
    {
        int thickest = player!.Layout.Displays.SelectMany(d => d.Strips).Where(s => s.DisplayId == displayId).Select(s => s.Thickness).DefaultIfEmpty(0).Max();
        return Math.Max(thickest, (int)(Math.Min(work.Width, work.Height) * 0.06));
    }

    /// <summary>Draws Home's magnified corners: each over the top-left part of its display, with the bezel around it.</summary>
    private void DrawLenses(DrawingContext drawingContext, Pen bezel)
    {
        foreach (Lens lens in lenses)
        {
            if (lens.View.Bitmap is not { } bitmap)
            {
                continue;
            }

            Rect area = lens.View.Map.ContentRect;
            var shadow = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));
            shadow.Freeze();
            Rect shadowRect = area;
            shadowRect.Offset(1.5, 1.5);
            drawingContext.DrawRoundedRectangle(shadow, null, shadowRect, BezelRadius, BezelRadius);
            drawingContext.PushClip(new RectangleGeometry(area, BezelRadius, BezelRadius));
            drawingContext.DrawImage(bitmap, area);
            drawingContext.Pop();
            drawingContext.DrawRoundedRectangle(null, bezel, area, BezelRadius, BezelRadius);
        }
    }

    private void DrawHighlight(DrawingContext drawingContext)
    {
        if (HighlightedSlot is not { } slot || map is null || player is null)
        {
            return;
        }

        Brush accent = (SystemParameters.HighContrast ? SystemColors.HighlightBrush : TryBrush(AccentKey)) ?? SystemColors.HighlightBrush;
        var pen = new Pen(accent, 2);
        foreach (DisplayScene display in shownDisplays.Where(d => d.Enabled || !ShowAllDisplays))
        {
            foreach (RectI rect in HighlightRects(display.Display, slot))
            {
                Rect dip = map.ToDip(rect);
                dip.Inflate(1, 1);
                drawingContext.DrawRectangle(null, pen, dip);
            }
        }
    }

    private IEnumerable<RectI> HighlightRects(DisplayInfo display, CellSlot slot)
    {
        RectI work = display.WorkArea;
        int band = BandThickness(display.DeviceId, work);
        if (slot.IsCorner())
        {
            BulbPlacement? corner = player!.Layout.Placements.FirstOrDefault(p => p.DisplayId == display.DeviceId && p.Slot == slot);
            if (corner is not null)
            {
                yield return corner.Bounds;
                yield break;
            }

            Corner c = slot.ToCorner();
            int x = c is Corner.TopLeft or Corner.BottomLeft ? work.Left : work.Right - band;
            int y = c is Corner.TopLeft or Corner.TopRight ? work.Top : work.Bottom - band;
            yield return RectI.FromXYWH(x, y, band, band);
            yield break;
        }

        Side side = slot.ToSide();
        bool any = false;
        foreach (StripLayout strip in player!.Layout.Displays.SelectMany(d => d.Strips).Where(s => s.DisplayId == display.DeviceId && s.Side == side && s.Count > 0))
        {
            any = true;
            yield return strip.Rect;
        }

        if (!any)
        {
            yield return side switch
            {
                Side.Top => new RectI(work.Left, work.Top, work.Right, work.Top + band),
                Side.Bottom => new RectI(work.Left, work.Bottom - band, work.Right, work.Bottom),
                Side.Left => new RectI(work.Left, work.Top, work.Left + band, work.Bottom),
                _ => new RectI(work.Right - band, work.Top, work.Right, work.Bottom),
            };
        }
    }

    private void DrawStatusText(DrawingContext drawingContext, double pixelsPerDip)
    {
        if (map is null || EffectiveServices is not { } services || shownDisplays.Count == 0)
        {
            return;
        }

        Rect first = VisibleDip(shownDisplays[0].Display);
        string? message = null;
        if (ShowStatusOverlays && services.Lights.Status.Health == LightsHealth.NoGraphicsDevice)
        {
            message = "Preview unavailable - reconnecting";
        }
        else if (ShowStatusOverlays && !ShowAllDisplays && !shownDisplays[0].Enabled)
        {
            message = "Not shown on this display";
        }
        else if (lightsOffPanel.Visibility != Visibility.Visible && player is { } current && current.Layout.Placements.Count == 0
                 && (Arrangement ?? services.Settings.Current.Current.Arrangement).HasNoBulbs())
        {
            message = EmptyText;
        }

        if (!string.IsNullOrEmpty(message))
        {
            Rect content = ShowAllDisplays ? map.ContentRect : first;
            DrawLabel(drawingContext, message, new Point(content.Left + content.Width / 2, content.Top + content.Height / 2), pixelsPerDip, alignBottom: false, centered: true, maxWidth: content.Width - 24);
        }
    }

    private static void DrawCentered(DrawingContext drawingContext, string text, Rect rect, Brush brush, double size, double pixelsPerDip)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, pixelsPerDip);
        drawingContext.DrawText(formatted, new Point(rect.Left + (rect.Width - formatted.Width) / 2, rect.Top + (rect.Height - formatted.Height) / 2));
    }

    /// <summary>Draws white text on the dark stage pill.</summary>
    private void DrawLabel(DrawingContext drawingContext, string text, Point anchor, double pixelsPerDip, bool alignBottom, bool centered = false, double maxWidth = double.PositiveInfinity, bool alignRight = false)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12,
            TryBrush(ThemeKeys.OnNightWellBrush) ?? Brushes.White, pixelsPerDip)
        {
            TextAlignment = TextAlignment.Left,
        };
        if (!double.IsInfinity(maxWidth) && maxWidth > 40)
        {
            formatted.MaxTextWidth = maxWidth - 20;
        }

        var box = new Rect(0, 0, formatted.Width + 20, formatted.Height + 8);
        box.Offset(
            centered ? anchor.X - box.Width / 2 : alignRight ? anchor.X - box.Width : anchor.X,
            centered ? anchor.Y - box.Height / 2 : alignBottom ? anchor.Y - box.Height : anchor.Y);
        Brush pill = TryBrush(PillKey) ?? new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20));
        drawingContext.DrawRoundedRectangle(pill, null, box, box.Height / 2, box.Height / 2);
        drawingContext.DrawText(formatted, new Point(box.Left + 10, box.Top + 4));
    }

    private double DesktopAspect()
    {
        if (EffectiveServices is not { } services)
        {
            return 16.0 / 9;
        }

        IEnumerable<RectI> bounds = ShowAllDisplays
            ? services.Displays.Displays.Select(d => d.Bounds)
            : [(services.Displays.Displays.FirstOrDefault(d => d.DeviceId == DisplayId) ?? services.Displays.Primary).Bounds];
        RectI union = default;
        foreach (RectI rect in bounds)
        {
            union = union.Union(rect);
        }

        return union.IsEmpty ? 16.0 / 9 : (double)union.Width / union.Height;
    }

    /// <summary>One picture of the stage (the main view, or a magnified corner) with its buffers and bitmap.</summary>
    private sealed class View(StageMap map, IReadOnlyList<DisplayScene> displays, LightsLayout layout)
    {
        public StageMap Map { get; } = map;

        public IReadOnlyList<DisplayScene> Displays { get; } = displays;

        public LightsLayout Layout { get; } = layout;

        /// <summary>The bands where bulbs and their glow are, in the view's pixels: the only parts that change between frames.</summary>
        public IReadOnlyList<RectI> DirtyRects { get; } = BulbBands(map, displays, layout);

        public PremultipliedImage? Base { get; set; }

        public bool BaseDirty { get; set; } = true;

        /// <summary>True when the base was composed before a display's backdrop was known (the night gradient stands in).</summary>
        public bool MissingBackdrop { get; set; }

        public PremultipliedImage? Buffer { get; set; }

        public WriteableBitmap? Bitmap { get; set; }

        public bool FullUpload { get; set; } = true;

        public ulong Signature { get; set; }

        private static List<RectI> BulbBands(StageMap map, IReadOnlyList<DisplayScene> displays, LightsLayout layout)
        {
            var shown = new HashSet<string>(displays.Select(d => d.Display.DeviceId), StringComparer.Ordinal);
            var all = new RectI(0, 0, map.PixelSize.Width, map.PixelSize.Height);
            var bands = new List<RectI>();
            foreach (IGrouping<(string DisplayId, int StripIndex), BulbPlacement> strip in layout.Placements
                         .Where(p => shown.Contains(p.DisplayId)).GroupBy(p => (p.DisplayId, p.StripIndex)))
            {
                RectI union = default;
                int largest = 0;
                foreach (BulbPlacement placement in strip)
                {
                    union = union.Union(placement.Bounds);
                    largest = Math.Max(largest, Math.Max(placement.Bounds.Width, placement.Bounds.Height));
                }

                RectI pixels = map.ToPixels(union);
                int margin = (int)Math.Ceiling(largest * map.Zoom * GlowReach) + 2;
                RectI band = new RectI(pixels.Left - margin, pixels.Top - margin, pixels.Right + margin, pixels.Bottom + margin).Intersect(all);
                if (!band.IsEmpty)
                {
                    bands.Add(band);
                }
            }

            return bands;
        }
    }

    /// <summary>A magnified corner of one of Home's displays.</summary>
    private sealed record Lens(DisplayScene Display, View View);

    /// <summary>Maps physical display pixels to a view's bitmap pixels and DIPs (a uniform fit, centred; or a zoomed region).</summary>
    private sealed class StageMap
    {
        /// <summary>Neighbouring displays are drawn inset by this much, so their bezels stay apart.</summary>
        private const double MultiDisplayInset = 1.5;

        private readonly double pixelsPerDip;
        private readonly bool inset;

        private StageMap(RectI region, bool inset, double zoom, SizeI pixelSize, Point contentOrigin, double pixelsPerDip, Corner? corner = null)
        {
            ZoomedCorner = corner;
            Region = region;
            this.inset = inset;
            this.pixelsPerDip = pixelsPerDip;
            Zoom = zoom;
            PixelSize = pixelSize;
            ContentRect = new Rect(contentOrigin, new Size(pixelSize.Width / pixelsPerDip, pixelSize.Height / pixelsPerDip));
            OffsetX = -region.Left * zoom;
            OffsetY = -region.Top * zoom;
        }

        /// <summary>The physical area shown (every display, or a corner of one).</summary>
        public RectI Region { get; }

        /// <summary>The display corner a zoomed view shows, or null when the whole display fits.</summary>
        public Corner? ZoomedCorner { get; }

        public double Zoom { get; }

        public SizeI PixelSize { get; }

        public Rect ContentRect { get; }

        public double OffsetX { get; }

        public double OffsetY { get; }

        public static StageMap? Create(IEnumerable<RectI> bounds, Size available, double pixelsPerDip)
        {
            RectI[] rects = [.. bounds];
            RectI union = default;
            foreach (RectI rect in rects)
            {
                union = union.Union(rect);
            }

            if (union.IsEmpty || available.Width < 1 || available.Height < 1)
            {
                return null;
            }

            double zoom = Math.Min(available.Width * pixelsPerDip / union.Width, available.Height * pixelsPerDip / union.Height);
            var size = new SizeI(
                Math.Max(1, (int)Math.Round(union.Width * zoom)),
                Math.Max(1, (int)Math.Round(union.Height * zoom)));
            var origin = new Point(
                Math.Round((available.Width - size.Width / pixelsPerDip) / 2 * pixelsPerDip) / pixelsPerDip,
                Math.Round((available.Height - size.Height / pixelsPerDip) / 2 * pixelsPerDip) / pixelsPerDip);
            return new StageMap(union, rects.Length > 1, zoom, size, origin, pixelsPerDip);
        }

        /// <summary>A corner of one display at a zoom, in the same picture area as the fitted display (the stage does not move).</summary>
        public static StageMap? CreateCorner(RectI display, Corner corner, double zoom, Size available, double pixelsPerDip)
        {
            StageMap? fit = Create([display], available, pixelsPerDip);
            if (fit is null || zoom <= fit.Zoom)
            {
                return fit;
            }

            RectI region = CornerRegion(display, corner, fit.PixelSize, zoom);
            return new StageMap(region, false, zoom, fit.PixelSize, fit.ContentRect.TopLeft, pixelsPerDip, corner);
        }

        /// <summary>The top-left corner of one display at a zoom, drawn into an area of the stage (Home's magnified corners).</summary>
        public static StageMap CreateLens(RectI display, double zoom, Rect area, double pixelsPerDip)
        {
            var size = new SizeI(Math.Max(1, (int)Math.Round(area.Width * pixelsPerDip)), Math.Max(1, (int)Math.Round(area.Height * pixelsPerDip)));
            return new StageMap(CornerRegion(display, Corner.TopLeft, size, zoom), false, zoom, size, area.TopLeft, pixelsPerDip);
        }

        public RectI ToPixels(RectI physical) => new(
            (int)Math.Floor((physical.Left - Region.Left) * Zoom + 0.5),
            (int)Math.Floor((physical.Top - Region.Top) * Zoom + 0.5),
            (int)Math.Floor((physical.Right - Region.Left) * Zoom + 0.5),
            (int)Math.Floor((physical.Bottom - Region.Top) * Zoom + 0.5));

        public Rect ToDip(RectI physical)
        {
            RectI pixels = ToPixels(physical);
            var rect = new Rect(ContentRect.Left + pixels.Left / pixelsPerDip, ContentRect.Top + pixels.Top / pixelsPerDip, pixels.Width / pixelsPerDip, pixels.Height / pixelsPerDip);
            if (inset)
            {
                rect.Inflate(-MultiDisplayInset, -MultiDisplayInset);
            }

            return rect;
        }

        public Point ToPhysical(Point dip) => new(
            (dip.X - ContentRect.Left) * pixelsPerDip / Zoom + Region.Left,
            (dip.Y - ContentRect.Top) * pixelsPerDip / Zoom + Region.Top);

        private static RectI CornerRegion(RectI display, Corner corner, SizeI pixels, double zoom)
        {
            int width = Math.Clamp((int)Math.Ceiling(pixels.Width / zoom), 1, display.Width);
            int height = Math.Clamp((int)Math.Ceiling(pixels.Height / zoom), 1, display.Height);
            int left = corner is Corner.TopLeft or Corner.BottomLeft ? display.Left : display.Right - width;
            int top = corner is Corner.TopLeft or Corner.TopRight ? display.Top : display.Bottom - height;
            return RectI.FromXYWH(left, top, width, height);
        }
    }

    /// <summary>The stage is a picture for screen readers; pages give it a name and a help text that summarizes the arrangement.</summary>
    private sealed class LightStageAutomationPeer(LightStage owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(LightStage);

        protected override bool IsControlElementCore() => true;

        protected override List<AutomationPeer> GetChildrenCore() =>
            ((LightStage)Owner).lightsOffPanel.Visibility == Visibility.Visible ? base.GetChildrenCore() ?? [] : [];
    }
}
