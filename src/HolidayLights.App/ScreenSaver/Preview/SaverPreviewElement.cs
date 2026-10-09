using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;

namespace HolidayLights.App.ScreenSaver.Preview;

/// <summary>
/// The live miniature of the screen saver on the Screen Saver page (PRODUCT-SPEC 3.5.3): the real simulation of the main
/// display at its DIP size, drawn scaled into the element (keeping the display's shape), restarted at once on every change
/// of what the saver shows, and animated (at up to 30 frames per second) only while the element is visible. In High
/// Contrast it draws no glow, like every UI preview (4.5).
/// </summary>
/// <remarks>
/// At the page's size a whole 4K display shrinks the bulbs to a few pixels, so the top-left corner of the same scene is
/// also drawn enlarged in an inset over that corner (PO decision 8): one display DIP per pixel, which shows the bulb art at
/// its own size times "Bulb Size". Both views follow one simulation, so the layout stays exact. UI thread.
/// </remarks>
internal sealed class SaverPreviewElement : FrameworkElement
{
    private const string LogSource = "ScreenSaver.Preview";
    private const double DefaultWidth = 480;

    /// <summary>The inset's share of the miniature's width and height.</summary>
    private const double InsetShare = 0.4;

    /// <summary>The inset is shown only when it magnifies at least this much.</summary>
    private const double MinimumMagnification = 2;

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30);
    private static readonly TimeSpan RebuildDelay = TimeSpan.FromMilliseconds(120);
    private static readonly Pen InsetBorder = CreateInsetBorder();

    private readonly SaverServices services;
    private readonly ISettingsStore settings;
    private readonly IDisplayService displays;
    private readonly ISystemInfo system;
    private readonly IMusicEventSource? music;
    private readonly DispatcherTimer rebuildTimer;
    private (string Id, DateTime Written, DecodedPicture? Picture)? pictureCache;
    private SaverRun? run;
    private CpuSaverRenderer? renderer;
    private WriteableBitmap? bitmap;
    private CpuSaverRenderer? insetRenderer;
    private WriteableBitmap? insetBitmap;
    private int buildVersion;
    private bool subscribed;
    private bool animating;
    private long lastFrame;

    /// <summary>Creates the preview.</summary>
    /// <param name="services">What it draws with.</param>
    /// <param name="settings">The settings it follows.</param>
    /// <param name="displays">The displays (the main display is previewed).</param>
    /// <param name="system">Whether High Contrast is on (no glow then).</param>
    /// <param name="music">Music events for "Dance to the Music", or null.</param>
    public SaverPreviewElement(SaverServices services, ISettingsStore settings, IDisplayService displays, ISystemInfo system, IMusicEventSource? music)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(system);
        this.services = services;
        this.settings = settings;
        this.displays = displays;
        this.system = system;
        this.music = music;
        rebuildTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = RebuildDelay };
        rebuildTimer.Tick += (_, _) => Rebuild();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(this, "Screen saver preview");
        AutomationProperties.SetHelpText(this, "The top-left corner is also shown enlarged, so you can see the bulbs.");
        Loaded += (_, _) => UpdateLifetime();
        Unloaded += (_, _) => UpdateLifetime();
        IsVisibleChanged += (_, _) => UpdateLifetime();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double aspect = Aspect();
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : DefaultWidth;
        double height = width * aspect;
        if (double.IsFinite(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height / aspect;
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        Rect area = PictureArea();
        drawingContext.DrawRectangle(Brushes.Black, null, area);
        if (bitmap is not null)
        {
            drawingContext.DrawImage(bitmap, area);
        }

        if (insetBitmap is not null)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            var inset = new Rect(area.Left, area.Top, insetBitmap.PixelWidth / dpi.DpiScaleX, insetBitmap.PixelHeight / dpi.DpiScaleY);
            drawingContext.DrawImage(insetBitmap, inset);
            Pen border = system.HighContrast ? new Pen(SystemColors.WindowTextBrush, 1) : InsetBorder;
            double half = border.Thickness / 2;
            drawingContext.DrawRectangle(null, border, new Rect(inset.Left + half, inset.Top + half, inset.Width - border.Thickness, inset.Height - border.Thickness));
        }
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ScheduleRebuild();
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ScheduleRebuild();
    }

    /// <summary>True when a change of the settings changes what the saver shows.</summary>
    private static bool ChangesTheSaver(AppSettings before, AppSettings after) =>
        before.Current.Saver != after.Current.Saver || before.Current.Arrangement != after.Current.Arrangement
        || before.Current.Flash != after.Current.Flash || before.Look != after.Look || before.Lights.Size != after.Lights.Size
        || before.Accessibility.LimitFlashing != after.Accessibility.LimitFlashing;

    private double Aspect()
    {
        RectI bounds = displays.Primary.Bounds;
        return bounds.Width > 0 && bounds.Height > 0 ? (double)bounds.Height / bounds.Width : 9.0 / 16.0;
    }

    /// <summary>The display's shape, centred in the element.</summary>
    private Rect PictureArea()
    {
        double aspect = Aspect();
        double width = Math.Min(RenderSize.Width, RenderSize.Height / aspect);
        double height = width * aspect;
        return new Rect((RenderSize.Width - width) / 2, (RenderSize.Height - height) / 2, width, height);
    }

    /// <summary>Runs while loaded and visible; follows settings and displays only then.</summary>
    private void UpdateLifetime()
    {
        bool active = IsLoaded && IsVisible;
        if (active && !subscribed)
        {
            subscribed = true;
            settings.Changed += OnSettingsChanged;
            displays.DisplaysChanged += OnDisplaysChanged;
            system.Changed += OnSystemChanged;
            Rebuild();
        }
        else if (!active && subscribed)
        {
            subscribed = false;
            settings.Changed -= OnSettingsChanged;
            displays.DisplaysChanged -= OnDisplaysChanged;
            system.Changed -= OnSystemChanged;
            rebuildTimer.Stop();
            Stop();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (ChangesTheSaver(e.OldSettings, e.NewSettings))
        {
            // "Updated instantly": a new picture of the saver at once; heavy work is debounced only while sizes settle.
            Rebuild();
        }
    }

    private void OnDisplaysChanged(object? sender, DisplaysChangedEventArgs e)
    {
        InvalidateMeasure();
        ScheduleRebuild();
    }

    private void OnSystemChanged(object? sender, EventArgs e) => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (subscribed)
        {
            rebuildTimer.Stop();
            rebuildTimer.Start();
        }
    }

    private void Stop()
    {
        if (animating)
        {
            CompositionTarget.Rendering -= OnRendering;
            animating = false;
        }

        run?.Dispose();
        run = null;
        renderer = null;
        insetRenderer = null;
    }

    /// <summary>Starts the preview again from the current settings (the picture is decoded off the UI thread).</summary>
    private async void Rebuild()
    {
        rebuildTimer.Stop();
        int version = ++buildVersion;
        try
        {
            AppSettings current = settings.Current;
            DecodedPicture? picture = await LoadPictureAsync(current.Current.Saver.Picture);
            if (version != buildVersion || !subscribed)
            {
                return;
            }

            Start(current, picture);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The screen saver preview couldn't be drawn.", ex);
            Stop();
            bitmap = null;
            insetBitmap = null;
            InvalidateVisual();
        }
    }

    private async Task<DecodedPicture?> LoadPictureAsync(string pictureId)
    {
        DateTime written = services.Pictures.TryGetPicture(pictureId, out PictureInfo? info) ? File.GetLastWriteTimeUtc(info.FilePath) : default;
        if (pictureCache is { } cached && cached.Id == pictureId && cached.Written == written)
        {
            return cached.Picture;
        }

        DecodedPicture? picture = await Task.Run(() => DecodedPicture.Load(services.Pictures, pictureId));
        pictureCache = (pictureId, written, picture);
        return picture;
    }

    private void Start(AppSettings current, DecodedPicture? picture)
    {
        Stop();
        insetBitmap = null;
        Rect area = PictureArea();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        DisplayInfo display = displays.Primary;
        if (area.Width < 1 || area.Height < 1 || display.Bounds.Width <= 0)
        {
            bitmap = null;
            InvalidateVisual();
            return;
        }

        uint seed = unchecked((uint)Stopwatch.GetTimestamp());
        SaverOptions options = SaverOptions.FromSettings(current, seed);
        if (system.HighContrast)
        {
            options = options with { GlowIntensity = 0 };
        }

        long start = Stopwatch.GetTimestamp();
        SaverScene scene = services.CreateScenes([new SaverDisplayPlan(display, true, true)], options, picture, seed, start)[0];
        var sprites = new SaverSpriteCache();
        double zoom = area.Width * dpi.DpiScaleX / display.Bounds.Width;
        renderer = services.CreateRenderer(scene, zoom, sprites);
        bitmap = CreateBitmap(renderer.Frame, dpi);
        if (InsetZoom(display, zoom) is { } insetZoom)
        {
            var corner = new RectI(
                0, 0,
                Math.Max(1, (int)Math.Round(renderer.Frame.Width * InsetShare, MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(renderer.Frame.Height * InsetShare, MidpointRounding.AwayFromZero)));
            insetRenderer = services.CreateRenderer(scene, insetZoom, sprites, corner);
            insetBitmap = CreateBitmap(insetRenderer.Frame, dpi);
        }

        run = new SaverRun([scene], start);
        run.ListenTo(music);
        DrawFrame();
        CompositionTarget.Rendering += OnRendering;
        animating = true;
        InvalidateVisual();
    }

    /// <summary>
    /// The zoom of the enlarged corner: one display DIP per pixel (the bulb art at its own size times "Bulb Size"), or null
    /// when the miniature itself is already large enough.
    /// </summary>
    /// <param name="display">The previewed display.</param>
    /// <param name="zoom">The miniature's zoom (preview pixels per display pixel).</param>
    /// <returns>The inset's zoom, or null for no inset.</returns>
    internal static double? InsetZoom(DisplayInfo display, double zoom)
    {
        ArgumentNullException.ThrowIfNull(display);
        double insetZoom = Math.Min(1, 1 / display.Scale);
        return insetZoom >= zoom * MinimumMagnification ? insetZoom : null;
    }

    private static WriteableBitmap CreateBitmap(PremultipliedImage frame, DpiScale dpi) =>
        new(frame.Width, frame.Height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32, null);

    private static Pen CreateInsetBorder()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), 1);
        pen.Freeze();
        return pen;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(lastFrame, now) < FrameInterval)
        {
            return;
        }

        try
        {
            DrawFrame();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The screen saver preview stopped.", ex);
            Stop();
        }
    }

    private void DrawFrame()
    {
        if (run is null || renderer is null || bitmap is null)
        {
            return;
        }

        lastFrame = Stopwatch.GetTimestamp();
        CpuSaverRenderer frame = renderer;
        CpuSaverRenderer? corner = insetRenderer;
        run.Advance(lastFrame, _ =>
        {
            frame.ApplyStep();
            corner?.ApplyStep();
        });
        Show(frame, run.Progress, bitmap);
        if (corner is not null && insetBitmap is not null)
        {
            Show(corner, run.Progress, insetBitmap);
        }
    }

    private static void Show(CpuSaverRenderer renderer, float progress, WriteableBitmap target)
    {
        renderer.Render(progress);
        PremultipliedImage pixels = renderer.Frame;
        target.WritePixels(new Int32Rect(0, 0, pixels.Width, pixels.Height), pixels.Pixels, pixels.Width * 4, 0);
    }
}
