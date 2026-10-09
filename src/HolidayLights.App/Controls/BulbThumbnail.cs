using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.App.Preview;

namespace HolidayLights.App.Controls;

/// <summary>
/// One picture of a bulb, fitted into the element and scaled with the current "Pixels" style (PRODUCT-SPEC 3.0.3): a chip's
/// edge art, a corner box's corner art, the 32 x 32 list preview of a Details row. Animated thumbnails step on the shared
/// UI clock (<see cref="BulbAnimationClock"/>) in unison with the lights; static ones show the lit frame.
/// </summary>
/// <remarks>
/// Damaged art shows the 5.4 WARNING picture (the catalog's placeholder cell). A bulb that cannot be resolved draws nothing
/// and sets <see cref="IsMissing"/> (the chip then shows its warning glyph).
/// </remarks>
public class BulbThumbnail : FrameworkElement, IStepAnimated
{
    /// <summary>Identifies <see cref="Services"/>.</summary>
    public static readonly DependencyProperty ServicesProperty =
        DependencyProperty.Register(nameof(Services), typeof(IAppServices), typeof(BulbThumbnail), new PropertyMetadata(null, OnInputChanged));

    /// <summary>Identifies <see cref="BulbId"/>.</summary>
    public static readonly DependencyProperty BulbIdProperty =
        DependencyProperty.Register(nameof(BulbId), typeof(string), typeof(BulbThumbnail), new PropertyMetadata(null, OnInputChanged));

    /// <summary>Identifies <see cref="Slot"/>.</summary>
    public static readonly DependencyProperty SlotProperty =
        DependencyProperty.Register(nameof(Slot), typeof(CellSlot), typeof(BulbThumbnail), new PropertyMetadata(CellSlot.Top, OnInputChanged));

    /// <summary>Identifies <see cref="Flavor"/>.</summary>
    public static readonly DependencyProperty FlavorProperty =
        DependencyProperty.Register(nameof(Flavor), typeof(int), typeof(BulbThumbnail), new PropertyMetadata(0, OnInputChanged));

    /// <summary>Identifies <see cref="IsAnimated"/>.</summary>
    public static readonly DependencyProperty IsAnimatedProperty =
        DependencyProperty.Register(nameof(IsAnimated), typeof(bool), typeof(BulbThumbnail), new PropertyMetadata(false, OnInputChanged));

    /// <summary>Identifies <see cref="MaxArtScale"/>.</summary>
    public static readonly DependencyProperty MaxArtScaleProperty =
        DependencyProperty.Register(nameof(MaxArtScale), typeof(double), typeof(BulbThumbnail), new PropertyMetadata(1.5, OnInputChanged));

    /// <summary>Identifies <see cref="ShowListPreview"/>.</summary>
    public static readonly DependencyProperty ShowListPreviewProperty =
        DependencyProperty.Register(nameof(ShowListPreview), typeof(bool), typeof(BulbThumbnail), new PropertyMetadata(false, OnInputChanged));

    /// <summary>Identifies <see cref="FallbackToListPreview"/>.</summary>
    public static readonly DependencyProperty FallbackToListPreviewProperty =
        DependencyProperty.Register(nameof(FallbackToListPreview), typeof(bool), typeof(BulbThumbnail), new PropertyMetadata(false, OnInputChanged));

    /// <summary>Identifies the inherited <see cref="PreviewAnimation.IsHotProperty"/> on thumbnails.</summary>
    public static readonly DependencyProperty IsHotProperty =
        PreviewAnimation.IsHotProperty.AddOwner(typeof(BulbThumbnail), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnInputChanged));

    private static readonly DependencyPropertyKey IsMissingPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMissing), typeof(bool), typeof(BulbThumbnail), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsMissing"/>.</summary>
    public static readonly DependencyProperty IsMissingProperty = IsMissingPropertyKey.DependencyProperty;

    /// <summary>Pictures whose bulb, slot and size were prepared once (every frame is in the caches): drawn on the UI thread at once.</summary>
    private static readonly ConcurrentDictionary<(string, CellSlot, int, double, bool, bool, Size, double, SpriteStyle), byte> Prepared = new();

    private IAppServices? subscribed;
    private BitmapSource? picture;
    private bool registered;
    private long shownStep;
    private int ticket;

    /// <summary><see cref="BulbId"/> for the catalog's events, which arrive on another thread (dependency properties are UI-thread only).</summary>
    private volatile string? watchedBulbId;

    static BulbThumbnail() => SnapsToDevicePixelsProperty.OverrideMetadata(typeof(BulbThumbnail), new FrameworkPropertyMetadata(true));

    /// <summary>Creates the thumbnail.</summary>
    public BulbThumbnail()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => UpdateRegistration();
    }

    /// <summary>The services, or null to use <see cref="AppServicesHost.Current"/>.</summary>
    public IAppServices? Services
    {
        get => (IAppServices?)GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    /// <summary>The bulb, or null for nothing.</summary>
    public string? BulbId
    {
        get => (string?)GetValue(BulbIdProperty);
        set => SetValue(BulbIdProperty, value);
    }

    /// <summary>Which picture: a side, a corner or the list preview.</summary>
    public CellSlot Slot
    {
        get => (CellSlot)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    /// <summary>The flavor (sides only).</summary>
    public int Flavor
    {
        get => (int)GetValue(FlavorProperty);
        set => SetValue(FlavorProperty, value);
    }

    /// <summary>Step through the frames with the lights (corner boxes); false shows the lit frame (chips).</summary>
    public bool IsAnimated
    {
        get => (bool)GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    /// <summary>The most DIPs per art pixel when fitting (corner boxes: 1.5).</summary>
    public double MaxArtScale
    {
        get => (double)GetValue(MaxArtScaleProperty);
        set => SetValue(MaxArtScaleProperty, value);
    }

    /// <summary>Shows the bulb's 32 x 32 list preview (the 5.4 white-square crop of the preview cell) at 1 DIP per art pixel.</summary>
    public bool ShowListPreview
    {
        get => (bool)GetValue(ShowListPreviewProperty);
        set => SetValue(ShowListPreviewProperty, value);
    }

    /// <summary>Shows the list preview when the chosen art is nearly empty (spacers).</summary>
    public bool FallbackToListPreview
    {
        get => (bool)GetValue(FallbackToListPreviewProperty);
        set => SetValue(FallbackToListPreviewProperty, value);
    }

    /// <summary>True when the bulb's file is missing (or damaged) and nothing can be drawn.</summary>
    public bool IsMissing => (bool)GetValue(IsMissingProperty);

    /// <inheritdoc />
    FrameworkElement IStepAnimated.Element => this;

    /// <summary>The services the thumbnail uses, or null when none are available.</summary>
    protected IAppServices? EffectiveServices => Services ?? (AppServicesHost.IsAvailable ? AppServicesHost.Current : null);

    /// <inheritdoc />
    void IStepAnimated.ShowStep(long step, StepClock clock)
    {
        if (step != shownStep)
        {
            shownStep = step;
            Redraw();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 32 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 32 : availableSize.Height);

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Redraw();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (picture is null)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double width = picture.PixelWidth / pixelsPerDip;
        double height = picture.PixelHeight / pixelsPerDip;
        double left = Math.Round((ActualWidth - width) / 2 * pixelsPerDip) / pixelsPerDip;
        double top = Math.Round((ActualHeight - height) / 2 * pixelsPerDip) / pixelsPerDip;
        drawingContext.DrawImage(picture, new Rect(left, top, width, height));
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new BulbThumbnailAutomationPeer(this);

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var thumbnail = (BulbThumbnail)d;
        if (e.Property == BulbIdProperty)
        {
            thumbnail.watchedBulbId = (string?)e.NewValue;
        }

        if (e.Property == ServicesProperty && thumbnail.IsLoaded)
        {
            thumbnail.Detach();
            thumbnail.Attach();
            return;
        }

        thumbnail.UpdateRegistration();
        thumbnail.Redraw();
    }

    private void Attach()
    {
        if (EffectiveServices is { } services && !ReferenceEquals(services, subscribed))
        {
            services.Bulbs.Changed += OnCatalogChanged;
            services.Settings.Changed += OnSettingsChanged;
            subscribed = services;
        }

        UpdateRegistration();
        Redraw();
    }

    private void Detach()
    {
        if (subscribed is not null)
        {
            if (registered)
            {
                BulbAnimationClock.For(subscribed).Unregister(this);
                registered = false;
            }

            subscribed.Bulbs.Changed -= OnCatalogChanged;
            subscribed.Settings.Changed -= OnSettingsChanged;
            subscribed = null;
        }
    }

    /// <summary>Raised on the catalog's thread (CONTRACTS 6.7): only <see cref="watchedBulbId"/> is read here, the redraw runs on the UI thread.</summary>
    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e)
    {
        if (watchedBulbId is { } id && e.BulbIds.Contains(id, BulbIds.Comparer))
        {
            foreach ((string, CellSlot, int, double, bool, bool, Size, double, SpriteStyle) key in Prepared.Keys)
            {
                if (BulbIds.Comparer.Equals(key.Item1, id))
                {
                    Prepared.TryRemove(key, out _);
                }
            }

            Dispatcher.InvokeAsync(Redraw);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Look.Pixels != e.NewSettings.Look.Pixels)
        {
            Redraw();
        }
    }

    private bool ShouldAnimate() => IsAnimated && IsVisible && IsLoaded && PreviewAnimation.ShouldStep(this);

    private void UpdateRegistration()
    {
        if (subscribed is null)
        {
            return;
        }

        bool animate = ShouldAnimate();
        if (animate && !registered)
        {
            registered = true;
            BulbAnimationClock.For(subscribed).Register(this);
        }
        else if (!animate && registered)
        {
            registered = false;
            BulbAnimationClock.For(subscribed).Unregister(this);
            Redraw();
        }
    }

    /// <summary>
    /// Shows the current picture. The first picture of a bulb at a size is prepared on the thread pool (reading the file,
    /// decoding and scaling every frame), so lists of thumbnails never block the UI thread; later pictures - the frames
    /// of an animated corner, a redraw - come from the caches at once.
    /// </summary>
    private void Redraw()
    {
        IAppServices? services = EffectiveServices;
        if (services is null || BulbId is not { } id || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            ticket++;
            Show(null, missing: false);
            return;
        }

        var inputs = new RenderInputs(id, Slot, Flavor, MaxArtScale, ShowListPreview, FallbackToListPreview, new Size(ActualWidth, ActualHeight),
            VisualTreeHelper.GetDpi(this), services.Settings.Current.Look.Pixels, ShouldAnimate() && registered ? shownStep : null);
        if (Prepared.ContainsKey(inputs.PreparedKey))
        {
            ticket++;
            (BitmapSource? image, bool missing) = Render(services, inputs, everyFrame: false);
            Show(image, missing);
            return;
        }

        int mine = ++ticket;
        Task.Run(() => Render(services, inputs, everyFrame: true)).ContinueWith(
            task =>
            {
                if (task.Exception is { } failure)
                {
                    services.Log.Warn("Settings.Preview", "A bulb picture could not be prepared.", failure.GetBaseException());
                }

                (BitmapSource? image, bool missing) = task.IsCompletedSuccessfully ? task.Result : (null, false);
                Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (mine == ticket)
                        {
                            Prepared.TryAdd(inputs.PreparedKey, 0);
                            Show(image, missing);
                        }
                    },
                    DispatcherPriority.Background);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Show(BitmapSource? image, bool missing)
    {
        picture = image;
        if (missing != IsMissing)
        {
            SetValue(IsMissingPropertyKey, missing);
        }

        InvalidateVisual();
    }

    /// <summary>Renders a picture (any thread); with <paramref name="everyFrame"/> it also prepares the other frames of the animation.</summary>
    private static (BitmapSource? Image, bool Missing) Render(IAppServices services, RenderInputs inputs, bool everyFrame)
    {
        if (!services.Bulbs.TryGetBulb(inputs.BulbId, out IBulb? bulb))
        {
            return (null, true);
        }

        DpiScale dpi = inputs.Dpi;
        double pixelsPerDip = dpi.PixelsPerDip;
        CellSlot slot = inputs.Slot;
        bool listPreview = inputs.ShowListPreview || (inputs.FallbackToListPreview && slot.IsSide() && StripModel.IsNearlyEmpty(bulb, slot.ToSide()));
        if (listPreview)
        {
            RectI window = bulb.PreviewWindow;
            double previewScale = Math.Min(
                Math.Min(inputs.Size.Width * pixelsPerDip / Math.Max(1, window.Width), inputs.Size.Height * pixelsPerDip / Math.Max(1, window.Height)),
                (inputs.ShowListPreview ? 1.0 : inputs.MaxArtScale) * pixelsPerDip);
            return (SpriteBitmaps.Get(services.Sprites, bulb, CellSlot.Preview, 0, 0, previewScale, inputs.Style, dpi, window), false);
        }

        BulbAnimationInfo animation = bulb.GetAnimation(slot, inputs.Flavor);
        int frame = inputs.Step is { } step
            ? (int)(((step % animation.FrameCount) + animation.FrameCount) % animation.FrameCount)
            : animation.Kind == BulbAnimationKind.LightBulb ? animation.LitFrame : 0;
        BitmapSource? image = Frame(services, bulb, inputs, frame);
        if (everyFrame)
        {
            for (int other = 0; other < animation.FrameCount; other++)
            {
                if (other != frame)
                {
                    Frame(services, bulb, inputs, other);
                }
            }
        }

        return (image, false);
    }

    private static BitmapSource? Frame(IAppServices services, IBulb bulb, RenderInputs inputs, int frame)
    {
        double pixelsPerDip = inputs.Dpi.PixelsPerDip;
        SizeI cell = bulb.GetCellSize(inputs.Slot, inputs.Flavor, frame);
        double scale = Math.Min(
            Math.Min(inputs.Size.Width * pixelsPerDip / Math.Max(1, cell.Width), inputs.Size.Height * pixelsPerDip / Math.Max(1, cell.Height)),
            inputs.MaxArtScale * pixelsPerDip);
        return SpriteBitmaps.Get(services.Sprites, bulb, inputs.Slot, inputs.Flavor, frame, Math.Max(scale, 0.01), inputs.Style, inputs.Dpi);
    }

    /// <summary>What a picture depends on, read on the UI thread.</summary>
    private sealed record RenderInputs(
        string BulbId, CellSlot Slot, int Flavor, double MaxArtScale, bool ShowListPreview, bool FallbackToListPreview, Size Size,
        DpiScale Dpi, SpriteStyle Style, long? Step)
    {
        /// <summary>Identifies the pictures a first render prepared (every frame of this bulb, slot and size).</summary>
        public (string, CellSlot, int, double, bool, bool, Size, double, SpriteStyle) PreparedKey =>
            (BulbId, Slot, Flavor, MaxArtScale, ShowListPreview, FallbackToListPreview, Size, Dpi.PixelsPerDip, Style);
    }

    /// <summary>Thumbnails are part of a named chip, tile or row; they are not separate elements for screen readers.</summary>
    private sealed class BulbThumbnailAutomationPeer(BulbThumbnail owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;

        protected override string GetClassNameCore() => nameof(BulbThumbnail);
    }
}
