using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.Preview;

/// <summary>When a <see cref="LightStrip"/> draws glow.</summary>
public enum StripGlow
{
    /// <summary>Only on dark backgrounds (dark mode), never in High Contrast (PRODUCT-SPEC 4.3.1).</summary>
    Auto,

    /// <summary>Always (night wells).</summary>
    On,

    /// <summary>Never.</summary>
    Off,
}

/// <summary>
/// A strip of lights (owner: settings-ui): one edge of an arrangement laid out by the real layout engine across the
/// element's width at its DPI, with the bulbs scaled so the tallest cell is <see cref="BulbHeight"/>, animated with the
/// current pattern. Used for the string of lights (4.3.1), the tray menu header (2.2, app-shell), edge samples and tile
/// runs (3.2.2, 3.2.5) and the Bulb Editing edge sample (3.3.1, bulb-factory).
/// </summary>
/// <remarks>
/// <para>Services come from <see cref="Services"/> or, when unset, <see cref="AppServicesHost.Current"/>.</para>
/// <para>By default the strip steps on the shared UI ticks (<see cref="BulbAnimationClock"/>: with the flash step, never
/// faster than 150 ms); <see cref="SmoothAnimation"/> adds the fades at up to 30 frames per second (the string of lights).
/// With Windows animation effects off it shows frame 0 unless its container is hot (<see cref="PreviewAnimation"/>). It
/// draws nothing new while its window is minimized or while the lights rest because nobody can see the screen.</para>
/// <para>A run of one bulb (<see cref="SingleBulbId"/>: tiles, the selected-bulb bar, the pattern demos) is laid out and
/// its art decoded and scaled on the thread pool, so a page full of tiles never blocks the UI thread; the night well
/// shows until the run is ready. Frames that repeat (a light bulb on, then off) are kept as frozen bitmaps and swapped
/// instead of being composited again, and a frame that did not change is not drawn again.</para>
/// </remarks>
public class LightStrip : FrameworkElement, IStepAnimated
{
    /// <summary>Identifies <see cref="Services"/>.</summary>
    public static readonly DependencyProperty ServicesProperty =
        DependencyProperty.Register(nameof(Services), typeof(IAppServices), typeof(LightStrip), new PropertyMetadata(null, OnLifetimeInputChanged));

    /// <summary>Identifies <see cref="Side"/>.</summary>
    public static readonly DependencyProperty SideProperty =
        DependencyProperty.Register(nameof(Side), typeof(Side), typeof(LightStrip), new PropertyMetadata(Side.Top, OnModelInputChanged));

    /// <summary>Identifies <see cref="IncludeCorners"/>.</summary>
    public static readonly DependencyProperty IncludeCornersProperty =
        DependencyProperty.Register(nameof(IncludeCorners), typeof(bool), typeof(LightStrip), new PropertyMetadata(true, OnModelInputChanged));

    /// <summary>Identifies <see cref="Arrangement"/>.</summary>
    public static readonly DependencyProperty ArrangementProperty =
        DependencyProperty.Register(nameof(Arrangement), typeof(SlotAssignment), typeof(LightStrip), new PropertyMetadata(null, OnModelInputChanged));

    /// <summary>Identifies <see cref="SingleBulbId"/>.</summary>
    public static readonly DependencyProperty SingleBulbIdProperty =
        DependencyProperty.Register(nameof(SingleBulbId), typeof(string), typeof(LightStrip), new PropertyMetadata(null, OnModelInputChanged));

    /// <summary>Identifies <see cref="Bulbs"/>.</summary>
    public static readonly DependencyProperty BulbsProperty =
        DependencyProperty.Register(nameof(Bulbs), typeof(IBulbResolver), typeof(LightStrip), new PropertyMetadata(null, OnModelInputChanged));

    /// <summary>Identifies <see cref="BulbHeight"/>.</summary>
    public static readonly DependencyProperty BulbHeightProperty =
        DependencyProperty.Register(nameof(BulbHeight), typeof(double), typeof(LightStrip), new PropertyMetadata(20.0, OnModelInputChanged));

    /// <summary>Identifies <see cref="IsAnimated"/>.</summary>
    public static readonly DependencyProperty IsAnimatedProperty =
        DependencyProperty.Register(nameof(IsAnimated), typeof(bool), typeof(LightStrip), new PropertyMetadata(true, OnLifetimeInputChanged));

    /// <summary>Identifies <see cref="Glow"/>.</summary>
    public static readonly DependencyProperty GlowProperty =
        DependencyProperty.Register(nameof(Glow), typeof(StripGlow), typeof(LightStrip), new PropertyMetadata(StripGlow.Auto, OnDrawInputChanged));

    /// <summary>Identifies <see cref="MaxArtScale"/>.</summary>
    public static readonly DependencyProperty MaxArtScaleProperty =
        DependencyProperty.Register(nameof(MaxArtScale), typeof(double), typeof(LightStrip), new PropertyMetadata(double.PositiveInfinity, OnModelInputChanged));

    /// <summary>Identifies <see cref="SmoothAnimation"/>.</summary>
    public static readonly DependencyProperty SmoothAnimationProperty =
        DependencyProperty.Register(nameof(SmoothAnimation), typeof(bool), typeof(LightStrip), new PropertyMetadata(false, OnLifetimeInputChanged));

    /// <summary>Identifies <see cref="FallbackToListPreview"/>.</summary>
    public static readonly DependencyProperty FallbackToListPreviewProperty =
        DependencyProperty.Register(nameof(FallbackToListPreview), typeof(bool), typeof(LightStrip), new PropertyMetadata(false, OnModelInputChanged));

    /// <summary>Identifies <see cref="CropRun"/>.</summary>
    public static readonly DependencyProperty CropRunProperty =
        DependencyProperty.Register(nameof(CropRun), typeof(bool), typeof(LightStrip), new PropertyMetadata(false, OnModelInputChanged));

    /// <summary>Identifies <see cref="Flash"/>.</summary>
    public static readonly DependencyProperty FlashProperty =
        DependencyProperty.Register(nameof(Flash), typeof(FlashOptions), typeof(LightStrip), new PropertyMetadata(null, OnModelInputChanged));

    /// <summary>Identifies <see cref="BulbCount"/>.</summary>
    public static readonly DependencyProperty BulbCountProperty =
        DependencyProperty.Register(nameof(BulbCount), typeof(int?), typeof(LightStrip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Identifies the inherited <see cref="PreviewAnimation.IsHotProperty"/> on strips.</summary>
    public static readonly DependencyProperty IsHotProperty =
        PreviewAnimation.IsHotProperty.AddOwner(typeof(LightStrip), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnLifetimeInputChanged));

    private static readonly DependencyPropertyKey HasBulbsPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasBulbs), typeof(bool), typeof(LightStrip),
            new PropertyMetadata(false, (d, _) => ((LightStrip)d).HasBulbsChanged?.Invoke(d, EventArgs.Empty)));

    /// <summary>Identifies <see cref="HasBulbs"/>.</summary>
    public static readonly DependencyProperty HasBulbsProperty = HasBulbsPropertyKey.DependencyProperty;

    private const string LogSource = "Settings.Preview";

    /// <summary>How many repeating frames a strip keeps (a light bulb's on and off, and a little more for chases).</summary>
    private const int FrameCacheSize = 3;

    private readonly DispatcherTimer smoothTimer;
    private readonly List<(ulong Signature, BitmapSource Bitmap)> frames = [];
    private IAppServices? subscribed;
    private Window? window;
    private StripModel? model;
    private PreviewPlayer? player;
    private BitmapSource? picture;
    private WriteableBitmap? writeable;
    private PremultipliedImage? buffer;
    private ulong shownSignature;
    private string? preparedBulbId;
    private int buildTicket;
    private bool registered;

    static LightStrip() => SnapsToDevicePixelsProperty.OverrideMetadata(typeof(LightStrip), new FrameworkPropertyMetadata(true));

    /// <summary>Creates the strip.</summary>
    public LightStrip()
    {
        smoothTimer = new DispatcherTimer(DispatcherPriority.Render);
        smoothTimer.Tick += (_, _) => DrawSmoothFrame();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    /// <summary>The services, or null to use <see cref="AppServicesHost.Current"/>.</summary>
    public IAppServices? Services
    {
        get => (IAppServices?)GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    /// <summary>Which edge (when the top edge is empty the string of lights uses the first non-empty edge).</summary>
    public Side Side
    {
        get => (Side)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    /// <summary>Draw the edge's corners at both ends (horizontal edges).</summary>
    public bool IncludeCorners
    {
        get => (bool)GetValue(IncludeCornersProperty);
        set => SetValue(IncludeCornersProperty, value);
    }

    /// <summary>The arrangement, or null for the desktop's current arrangement.</summary>
    public SlotAssignment? Arrangement
    {
        get => (SlotAssignment?)GetValue(ArrangementProperty);
        set => SetValue(ArrangementProperty, value);
    }

    /// <summary>A run of one bulb (its flavors side by side at its real spacing: tiles, chips, Bulb Editing), or null to use <see cref="Arrangement"/>.</summary>
    public string? SingleBulbId
    {
        get => (string?)GetValue(SingleBulbIdProperty);
        set => SetValue(SingleBulbIdProperty, value);
    }

    /// <summary>A resolver that knows extra (transient) bulbs, e.g. the unsaved Bulb Editing document; null = the catalog.</summary>
    public IBulbResolver? Bulbs
    {
        get => (IBulbResolver?)GetValue(BulbsProperty);
        set => SetValue(BulbsProperty, value);
    }

    /// <summary>Height in DIP of the tallest cell (20 for the string of lights and the tray header).</summary>
    public double BulbHeight
    {
        get => (double)GetValue(BulbHeightProperty);
        set => SetValue(BulbHeightProperty, value);
    }

    /// <summary>Animate with the pattern; false shows frame 0, lit.</summary>
    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <summary>When to draw glow.</summary>
    public StripGlow Glow
    {
        get => (StripGlow)GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    /// <summary>The most DIPs per art pixel (tiles: twice the bulb size setting); unlimited by default.</summary>
    public double MaxArtScale
    {
        get => (double)GetValue(MaxArtScaleProperty);
        set => SetValue(MaxArtScaleProperty, value);
    }

    /// <summary>Draw the fades between steps at up to 30 frames per second (the string of lights) instead of stepping on the shared ticks.</summary>
    public bool SmoothAnimation
    {
        get => (bool)GetValue(SmoothAnimationProperty);
        set => SetValue(SmoothAnimationProperty, value);
    }

    /// <summary>When the run of a single bulb is nearly empty (spacers), show the bulb's 32 x 32 list preview instead (tiles).</summary>
    public bool FallbackToListPreview
    {
        get => (bool)GetValue(FallbackToListPreviewProperty);
        set => SetValue(FallbackToListPreviewProperty, value);
    }

    /// <summary>
    /// With <see cref="SingleBulbId"/>: lay the run out longer than the strip and crop it at the far end (Bulb List tiles,
    /// PRODUCT-SPEC 3.2.5), so the bulb's flavors show side by side at its real spacing instead of one whole bulb and a gap.
    /// </summary>
    public bool CropRun
    {
        get => (bool)GetValue(CropRunProperty);
        set => SetValue(CropRunProperty, value);
    }

    /// <summary>The pattern to show (the Flash Settings demos), or null for the desktop's effective options.</summary>
    public FlashOptions? Flash
    {
        get => (FlashOptions?)GetValue(FlashProperty);
        set => SetValue(FlashProperty, value);
    }

    /// <summary>With <see cref="SingleBulbId"/>: the strip's natural length holds this many bulbs (the pattern demos show five).</summary>
    public int? BulbCount
    {
        get => (int?)GetValue(BulbCountProperty);
        set => SetValue(BulbCountProperty, value);
    }

    /// <summary>True while the strip has something to draw (an empty edge without corners collapses the string of lights).</summary>
    public bool HasBulbs => (bool)GetValue(HasBulbsProperty);

    /// <summary>Raised when <see cref="HasBulbs"/> changes.</summary>
    public event EventHandler? HasBulbsChanged;

    /// <inheritdoc />
    FrameworkElement IStepAnimated.Element => this;

    /// <summary>The layout on display (physical pixels of the strip), for tests.</summary>
    public LightsLayout? CurrentLayout => model?.Layout;

    /// <summary>True while a single-bulb run is being prepared on the thread pool (for tests).</summary>
    public bool IsPreparing { get; private set; }

    /// <summary>How many frames were composited (not taken from the frame cache or skipped), for tests.</summary>
    internal int CompositedFrames { get; private set; }

    /// <summary>True while the strip draws new frames (its fade timer runs, or it steps on the shared ticks), for tests.</summary>
    internal bool IsAnimating => smoothTimer.IsEnabled || registered;

    /// <summary>The services the strip uses, or null when none are available.</summary>
    protected IAppServices? EffectiveServices => Services ?? (AppServicesHost.IsAvailable ? AppServicesHost.Current : null);

    /// <inheritdoc />
    void IStepAnimated.ShowStep(long step, StepClock clock)
    {
        if (model is null || player is null)
        {
            return;
        }

        BulbVisualState[] states = player.Sample(clock.TimestampOfStep(step + 1) - 1, clock);
        Draw(states, cacheable: true);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!HasBulbs && model is not null)
        {
            return default;
        }

        bool horizontal = Side is Side.Top or Side.Bottom;
        double thickness = BulbHeight;
        double length = horizontal ? availableSize.Width : availableSize.Height;
        if (NaturalLength() is { } natural)
        {
            length = Math.Min(natural, length);
        }
        else if (double.IsInfinity(length))
        {
            length = thickness * 6;
        }

        return horizontal ? new Size(length, Math.Min(thickness, availableSize.Height)) : new Size(Math.Min(thickness, availableSize.Width), length);
    }

    /// <summary>The length in DIP of <see cref="BulbCount"/> bulbs of <see cref="SingleBulbId"/> at the strip's scale.</summary>
    private double? NaturalLength()
    {
        if (BulbCount is not { } count || SingleBulbId is not { } id || EffectiveServices is not { } services
            || !(Bulbs ?? services.Bulbs).TryGetBulb(id, out IBulb? bulb))
        {
            return null;
        }

        bool horizontal = Side is Side.Top or Side.Bottom;
        SizeI cell = bulb.GetCellSize(Side.ToSlot(), 0, 0);
        int spacing = horizontal ? bulb.HorizontalSpacing : bulb.VerticalSpacing;
        int across = horizontal ? cell.Height : cell.Width;
        int along = (horizontal ? cell.Width : cell.Height) + 2 * spacing;
        double scale = Math.Min(BulbHeight / Math.Max(1, across), MaxArtScale);
        return Math.Ceiling(count * along * scale) + 1;
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
        if (picture is null)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        drawingContext.DrawImage(picture, new Rect(0, 0, picture.PixelWidth / pixelsPerDip, picture.PixelHeight / pixelsPerDip));
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new LightStripAutomationPeer(this);

    private static void OnModelInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LightStrip)d).Rebuild();

    private static void OnDrawInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LightStrip)d).DrawCurrent();

    private static void OnLifetimeInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (LightStrip)d;
        if (e.Property == ServicesProperty && strip.IsLoaded)
        {
            strip.Detach();
            strip.Attach();
            return;
        }

        strip.UpdateAnimation();
        strip.DrawCurrent();
    }

    private void Attach()
    {
        if (EffectiveServices is { } services && !ReferenceEquals(services, subscribed))
        {
            services.Lights.SceneChanged += OnSceneChanged;
            services.Lights.StatusChanged += OnStatusChanged;
            services.Lights.Clock.ClockChanged += OnClockChanged;
            services.Bulbs.Changed += OnCatalogChanged;
            subscribed = services;
        }

        Window? host = Window.GetWindow(this);
        if (!ReferenceEquals(host, window))
        {
            if (window is not null)
            {
                window.StateChanged -= OnWindowStateChanged;
            }

            window = host;
            if (window is not null)
            {
                window.StateChanged += OnWindowStateChanged;
            }
        }

        Rebuild();
    }

    private void Detach()
    {
        StopAnimation();
        buildTicket++;
        IsPreparing = false;
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

    /// <summary>The lights started or stopped resting: stop, or show the current step again and go on.</summary>
    private void OnStatusChanged(object? sender, EventArgs e)
    {
        UpdateAnimation();
        DrawCurrent();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateAnimation();
        DrawCurrent();
    }

    private void OnClockChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(UpdateAnimation);

    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e)
    {
        if (model is not null && e.BulbIds.Any(id => model.UsesBulb(id)))
        {
            Dispatcher.InvokeAsync(Rebuild);
        }
    }

    private void Rebuild()
    {
        if (!IsLoaded || EffectiveServices is not { } services)
        {
            return;
        }

        int ticket = ++buildTicket;
        StripSpec spec = StripSpec.From(services, this, VisualTreeHelper.GetDpi(this).PixelsPerDip, RenderSize);
        if (spec.SingleBulbId is { } bulbId)
        {
            PrepareInBackground(services, spec, bulbId, ticket);
            return;
        }

        StripModel? built;
        try
        {
            built = StripModel.Create(services, spec);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            services.Log.Warn(LogSource, "A strip of lights could not be laid out.", exception);
            built = null;
        }

        Install(services, built);
    }

    /// <summary>Lays out a single-bulb run and prepares its sprites on the thread pool, then shows it (if still wanted).</summary>
    private void PrepareInBackground(IAppServices services, StripSpec spec, string bulbId, int ticket)
    {
        if (!string.Equals(preparedBulbId, bulbId, StringComparison.Ordinal))
        {
            // Another bulb (a recycled tile): show the empty night well rather than the previous bulb meanwhile.
            StopAnimation();
            player?.Dispose();
            player = null;
            model = null;
            picture = null;
            frames.Clear();
            InvalidateVisual();
        }

        IsPreparing = true;
        bool glow = GlowIntensity(services) > 0;
        Task.Run(() =>
        {
            try
            {
                StripModel built = StripModel.Create(services, spec);
                built.WarmSprites(services, glow);
                return built;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException)
            {
                services.Log.Warn(LogSource, "A strip of lights could not be laid out.", exception);
                return null;
            }
        }).ContinueWith(
            task =>
            {
                if (task.Exception is { } failure)
                {
                    services.Log.Warn(LogSource, "A strip of lights could not be prepared.", failure.GetBaseException());
                }

                StripModel? built = task.IsCompletedSuccessfully ? task.Result : null;
                Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (ticket != buildTicket || !IsLoaded)
                        {
                            return;
                        }

                        IsPreparing = false;
                        preparedBulbId = bulbId;
                        Install(services, built);
                    },
                    DispatcherPriority.Background);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Install(IAppServices services, StripModel? built)
    {
        model = built;
        frames.Clear();
        picture = null;
        bool hasBulbs = model is { IsEmpty: false };
        if (hasBulbs != HasBulbs)
        {
            SetValue(HasBulbsPropertyKey, hasBulbs);
            InvalidateMeasure();
        }

        player?.Dispose();
        player = model is null ? null : new PreviewPlayer(services.Flash, model.Bulbs, model.Layout, Flash ?? services.Lights.Scene.Flash, null);
        UpdateAnimation();
        DrawCurrent();
    }

    private bool ShouldAnimate() =>
        IsAnimated && IsVisible && player is { IsStatic: false } && PreviewAnimation.ShouldStep(this) && !SystemParameters.HighContrast
        && !PreviewActivity.IsMinimized(window) && EffectiveServices is { } services && !PreviewActivity.IsResting(services);

    private void UpdateAnimation()
    {
        StopAnimation();
        if (!ShouldAnimate() || EffectiveServices is not { } services)
        {
            return;
        }

        if (SmoothAnimation)
        {
            ScheduleSmooth(services);
        }
        else
        {
            BulbAnimationClock.For(services).Register(this);
            registered = true;
        }
    }

    private void StopAnimation()
    {
        smoothTimer.Stop();
        if (registered && subscribed is not null)
        {
            BulbAnimationClock.For(subscribed).Unregister(this);
        }

        registered = false;
    }

    private void ScheduleSmooth(IAppServices services)
    {
        long now = Stopwatch.GetTimestamp();
        long next = player!.NextRedraw(now, services.Lights.Clock.Clock);
        if (next == long.MaxValue)
        {
            return;
        }

        smoothTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((next - now) * 1000.0 / Stopwatch.Frequency, 1, 1000));
        smoothTimer.Start();
    }

    private void DrawSmoothFrame()
    {
        smoothTimer.Stop();
        if (player is null || EffectiveServices is not { } services)
        {
            return;
        }

        BulbVisualState[] states = player.Sample(Stopwatch.GetTimestamp(), services.Lights.Clock.Clock);
        Draw(states, cacheable: IsSteady(states));
        if (ShouldAnimate())
        {
            ScheduleSmooth(services);
        }
    }

    /// <summary>True when no bulb is part-way through a fade: such frames repeat from step to step and are worth keeping.</summary>
    private static bool IsSteady(BulbVisualState[] states)
    {
        foreach (BulbVisualState state in states)
        {
            if (state.Brightness is not (0f or 1f) || state.Glow is not (0f or 1f))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Draws the current state: the pattern's state now when animating, else frame 0 lit.</summary>
    private void DrawCurrent()
    {
        if (model is null || EffectiveServices is not { } services)
        {
            if (!IsPreparing)
            {
                picture = null;
                InvalidateVisual();
            }

            return;
        }

        if (ShouldAnimate() && player is not null)
        {
            StepClock clock = services.Lights.Clock.Clock;
            long now = Stopwatch.GetTimestamp();
            if (SmoothAnimation)
            {
                BulbVisualState[] states = player.Sample(now, clock);
                Draw(states, cacheable: IsSteady(states));
            }
            else
            {
                Draw(player.Sample(clock.TimestampOfStep(clock.StepAt(now) + 1) - 1, clock), cacheable: true);
            }
        }
        else
        {
            Draw(ScenePreviewRenderer.CreateStaticStates(model.Layout, model.Bulbs), cacheable: true);
        }
    }

    /// <summary>
    /// Shows a frame: nothing when it equals the frame on screen, a kept bitmap when the frame repeats, else composites it
    /// (into a kept frozen bitmap for step frames, into the one writeable bitmap for the fades in between).
    /// </summary>
    private void Draw(IReadOnlyList<BulbVisualState> states, bool cacheable)
    {
        // Nobody can see the screen while the lights rest: the end of the rest draws the current frame.
        if (model is null || EffectiveServices is not { } services || PreviewActivity.IsResting(services))
        {
            return;
        }

        float glow = GlowIntensity(services);
        ulong signature = FrameSignature.Of(states, glow);
        if (picture is not null && signature == shownSignature)
        {
            return;
        }

        if (cacheable && FindFrame(signature) is { } kept)
        {
            Show(kept, signature);
            return;
        }

        if (buffer is null || buffer.Width != model.PixelSize.Width || buffer.Height != model.PixelSize.Height)
        {
            buffer = new PremultipliedImage(model.PixelSize.Width, model.PixelSize.Height);
        }
        else
        {
            Array.Clear(buffer.Pixels);
        }

        model.Draw(buffer, states, services, glow);
        CompositedFrames++;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        if (cacheable)
        {
            BitmapSource frozen = PixelBuffers.ToFrozenBitmap(buffer, dpi);
            frames.Insert(0, (signature, frozen));
            if (frames.Count > FrameCacheSize)
            {
                frames.RemoveAt(frames.Count - 1);
            }

            Show(frozen, signature);
        }
        else
        {
            writeable = PixelBuffers.Present(writeable, buffer, dpi);
            Show(writeable, signature);
        }
    }

    private BitmapSource? FindFrame(ulong signature)
    {
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].Signature == signature)
            {
                (ulong Signature, BitmapSource Bitmap) hit = frames[i];
                frames.RemoveAt(i);
                frames.Insert(0, hit);
                return hit.Bitmap;
            }
        }

        return null;
    }

    private void Show(BitmapSource bitmap, ulong signature)
    {
        picture = bitmap;
        shownSignature = signature;
        InvalidateVisual();
    }

    private float GlowIntensity(IAppServices services)
    {
        if (SystemParameters.HighContrast || Glow == StripGlow.Off)
        {
            return 0f;
        }

        float intensity = GlowLevels.Intensity(services.Lights.Scene.Effects.Glow);
        return Glow == StripGlow.On || IsDarkBackground() ? intensity : 0f;
    }

    /// <summary>True in dark mode: the primary text colour of the Fluent theme is light.</summary>
    private bool IsDarkBackground() =>
        TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush { Color: var color } && 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 128;

    /// <summary>Exposes nothing to screen readers: strips are decoration (the string of lights) or part of a named tile.</summary>
    private sealed class LightStripAutomationPeer(LightStrip owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;

        protected override string GetClassNameCore() => nameof(LightStrip);
    }
}
