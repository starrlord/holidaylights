using System.Globalization;
using System.Windows.Media;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// What the Bulb Editing preview shows (PRODUCT-SPEC 3.3.1): one animation drawn by the faithful decoder, its frame (the
/// next one at every flash step, or by hand while paused; always frame 0 for the bulb list preview, as in 5.4), the zoom
/// (1x-8x; the largest that fits until the user picks one), the transparency checkerboard and the GIF tip.
/// </summary>
/// <remarks>UI thread.</remarks>
internal sealed class PreviewViewModel : ObservableObject
{
    private const int MinZoom = 1;
    private const int MaxZoom = 8;

    /// <summary>Space kept around the picture when the zoom is chosen to fit, in DIP.</summary>
    private const double FitMargin = 16;

    private readonly HashSet<BulbGif> dismissedTips = [];
    private EditorAnimation? animation;
    private SizeI animationSize;
    private bool firstFrameOnly;
    private int frameIndex;
    private bool isPaused;
    private int zoom = MinZoom;
    private bool zoomChosen;
    private double viewportWidth;
    private double viewportHeight;
    private bool showTransparency;
    private bool isGifTipOpen;

    /// <summary>Creates the preview (nothing shown yet).</summary>
    public PreviewViewModel()
    {
        ZoomInCommand = new RelayCommand(() => ChooseZoom(zoom + 1), () => zoom < MaxZoom);
        ZoomOutCommand = new RelayCommand(() => ChooseZoom(zoom - 1), () => zoom > MinZoom);
    }

    /// <summary>The shown animation, or null.</summary>
    public EditorAnimation? Animation => animation;

    /// <summary>The frame to draw, or null when there is no picture to show.</summary>
    public ImageSource? CurrentFrame => animation is { IsDamaged: false } shown ? shown.Frames[ShownFrame(shown)] : null;

    /// <summary>Width of the shown picture in art pixels.</summary>
    public int PictureWidth => animation?.Size.Width ?? 0;

    /// <summary>Height of the shown picture in art pixels.</summary>
    public int PictureHeight => animation?.Size.Height ?? 0;

    /// <summary>The zoom, 1-8.</summary>
    public int Zoom
    {
        get => zoom;
        private set
        {
            if (SetProperty(ref zoom, Math.Clamp(value, MinZoom, MaxZoom)))
            {
                OnPropertyChanged(nameof(ZoomText));
                ZoomInCommand.Refresh();
                ZoomOutCommand.Refresh();
            }
        }
    }

    /// <summary>"2x".</summary>
    public string ZoomText => string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.ZoomFormat, zoom);

    /// <summary>Zoom in.</summary>
    public RelayCommand ZoomInCommand { get; }

    /// <summary>Zoom out.</summary>
    public RelayCommand ZoomOutCommand { get; }

    /// <summary>"Frame 1 of 4" (empty without a picture).</summary>
    public string FrameText => animation is { IsDamaged: false } shown
        ? string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.FrameFormat, ShownFrame(shown) + 1, shown.Frames.Count)
        : "";

    /// <summary>The preview stands still ("Pause"); Left and Right then step through the frames.</summary>
    public bool IsPaused
    {
        get => isPaused;
        set => SetProperty(ref isPaused, value);
    }

    /// <summary>"Transparency": a checkerboard behind the picture instead of the night sky.</summary>
    public bool ShowTransparency
    {
        get => showTransparency;
        set => SetProperty(ref showTransparency, value);
    }

    /// <summary>The text of the GIF tip.</summary>
    public string GifTip => BulbFactoryStrings.GifTip;

    /// <summary>The GIF tip shows (the shown GIF looks different in a standard viewer); closing it hides it for that GIF.</summary>
    public bool IsGifTipOpen
    {
        get => isGifTipOpen;
        set
        {
            if (SetProperty(ref isGifTipOpen, value) && !value && animation is not null)
            {
                dismissedTips.Add(animation.Gif);
            }
        }
    }

    private bool CanStep => !firstFrameOnly && animation is { IsDamaged: false, Frames.Count: > 1 };

    /// <summary>Shows an animation (null: nothing), keeping the frame number where it can, fitting the zoom to a new size.</summary>
    /// <param name="shown">The animation.</param>
    /// <param name="onlyFirstFrame">True for the bulb list preview: frame 0 only (5.4).</param>
    public void Show(EditorAnimation? shown, bool onlyFirstFrame)
    {
        animation = shown;
        firstFrameOnly = onlyFirstFrame;
        if (shown is not null && shown.Size != animationSize)
        {
            animationSize = shown.Size;
            zoomChosen = false;
        }

        if (!zoomChosen)
        {
            Zoom = FitZoom();
        }

        frameIndex = shown is null ? 0 : frameIndex % shown.Frames.Count;
        isGifTipOpen = shown is { DiffersFromStandard: true } && !dismissedTips.Contains(shown.Gif);
        foreach (string property in (string[])[nameof(Animation), nameof(CurrentFrame), nameof(PictureWidth), nameof(PictureHeight), nameof(FrameText), nameof(IsGifTipOpen)])
        {
            OnPropertyChanged(property);
        }
    }

    /// <summary>Starts again at frame 1 (another slot or flavor was chosen).</summary>
    public void Restart() => SetFrame(0);

    /// <summary>One flash step (the window's timer, at the flash speed): the next frame unless paused.</summary>
    public void Tick()
    {
        if (!isPaused && CanStep)
        {
            SetFrame((frameIndex + 1) % animation!.Frames.Count);
        }
    }

    /// <summary>Left or Right while paused: the previous or next frame.</summary>
    /// <param name="delta">-1 or 1.</param>
    /// <returns>True when the frame changed.</returns>
    public bool StepFrame(int delta)
    {
        if (!isPaused || !CanStep)
        {
            return false;
        }

        int count = animation!.Frames.Count;
        SetFrame(((frameIndex + delta) % count + count) % count);
        return true;
    }

    /// <summary>The space the preview can use; the zoom fits the picture into it until the user picks one.</summary>
    /// <param name="width">Width in DIP.</param>
    /// <param name="height">Height in DIP.</param>
    public void SetViewport(double width, double height)
    {
        viewportWidth = width;
        viewportHeight = height;
        if (!zoomChosen)
        {
            Zoom = FitZoom();
        }
    }

    private int ShownFrame(EditorAnimation shown) => firstFrameOnly ? 0 : frameIndex % shown.Frames.Count;

    private void ChooseZoom(int value)
    {
        zoomChosen = true;
        Zoom = value;
    }

    private int FitZoom()
    {
        if (animation is null || viewportWidth <= FitMargin || viewportHeight <= FitMargin)
        {
            return zoom;
        }

        double fit = Math.Min((viewportWidth - FitMargin) / animation.Size.Width, (viewportHeight - FitMargin) / animation.Size.Height);
        return Math.Clamp((int)Math.Floor(fit), MinZoom, MaxZoom);
    }

    private void SetFrame(int index)
    {
        frameIndex = index;
        OnPropertyChanged(nameof(CurrentFrame));
        OnPropertyChanged(nameof(FrameText));
    }
}
