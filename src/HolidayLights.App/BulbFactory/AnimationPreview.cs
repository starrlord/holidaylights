using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Imaging;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// The Bulb Editing preview (PRODUCT-SPEC 3.3.1): one frame at an integer zoom with crisp pixels, on a checkerboard when
/// "Transparency" is on, and for the bulb list preview the white square: everything outside the 32 x 32 list picture is
/// dimmed with the 5.4 <c>DITHER</c> pattern, with a black inner and a white outer outline.
/// Dragging moves the square (<see cref="SquareDragged"/> reports the pointer in art pixels).
/// </summary>
internal sealed class AnimationPreview : FrameworkElement
{
    /// <summary>Identifies <see cref="Frame"/>.</summary>
    public static readonly DependencyProperty FrameProperty = Register(nameof(Frame), typeof(ImageSource), null, FrameworkPropertyMetadataOptions.AffectsRender);

    /// <summary>Identifies <see cref="PictureWidth"/>.</summary>
    public static readonly DependencyProperty PictureWidthProperty = Register(nameof(PictureWidth), typeof(int), 0, FrameworkPropertyMetadataOptions.AffectsMeasure);

    /// <summary>Identifies <see cref="PictureHeight"/>.</summary>
    public static readonly DependencyProperty PictureHeightProperty = Register(nameof(PictureHeight), typeof(int), 0, FrameworkPropertyMetadataOptions.AffectsMeasure);

    /// <summary>Identifies <see cref="Zoom"/>.</summary>
    public static readonly DependencyProperty ZoomProperty = Register(nameof(Zoom), typeof(int), 1, FrameworkPropertyMetadataOptions.AffectsMeasure);

    /// <summary>Identifies <see cref="ShowTransparency"/>.</summary>
    public static readonly DependencyProperty ShowTransparencyProperty = Register(nameof(ShowTransparency), typeof(bool), false, FrameworkPropertyMetadataOptions.AffectsRender);

    /// <summary>Identifies <see cref="ShowWhiteSquare"/>.</summary>
    public static readonly DependencyProperty ShowWhiteSquareProperty = Register(nameof(ShowWhiteSquare), typeof(bool), false, FrameworkPropertyMetadataOptions.AffectsRender);

    /// <summary>Identifies <see cref="WhiteSquare"/>.</summary>
    public static readonly DependencyProperty WhiteSquareProperty = Register(nameof(WhiteSquare), typeof(RectI), default(RectI), FrameworkPropertyMetadataOptions.AffectsRender);

    private const int CheckerSize = 8;

    private static readonly Brush Checkerboard = CreateCheckerboard();
    private static readonly BitmapSource DitherBitmap = CreateDitherBitmap();

    private bool dragging;

    static AnimationPreview() => FocusableProperty.OverrideMetadata(typeof(AnimationPreview), new FrameworkPropertyMetadata(true));

    /// <summary>Creates the preview (pixels are scaled without smoothing).</summary>
    public AnimationPreview() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

    /// <summary>Raised while the square is dragged, with the pointer position in art pixels.</summary>
    public event EventHandler<Point>? SquareDragged;

    /// <summary>The frame to draw (null: nothing).</summary>
    public ImageSource? Frame
    {
        get => (ImageSource?)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    /// <summary>Picture width in art pixels.</summary>
    public int PictureWidth
    {
        get => (int)GetValue(PictureWidthProperty);
        set => SetValue(PictureWidthProperty, value);
    }

    /// <summary>Picture height in art pixels.</summary>
    public int PictureHeight
    {
        get => (int)GetValue(PictureHeightProperty);
        set => SetValue(PictureHeightProperty, value);
    }

    /// <summary>DIP per art pixel (1-8).</summary>
    public int Zoom
    {
        get => (int)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Draw a checkerboard behind the picture.</summary>
    public bool ShowTransparency
    {
        get => (bool)GetValue(ShowTransparencyProperty);
        set => SetValue(ShowTransparencyProperty, value);
    }

    /// <summary>Draw the white square and dim the rest.</summary>
    public bool ShowWhiteSquare
    {
        get => (bool)GetValue(ShowWhiteSquareProperty);
        set => SetValue(ShowWhiteSquareProperty, value);
    }

    /// <summary>The list-preview window in art pixels.</summary>
    public RectI WhiteSquare
    {
        get => (RectI)GetValue(WhiteSquareProperty);
        set => SetValue(WhiteSquareProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(PictureWidth * Zoom, PictureHeight * Zoom);

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        int zoom = Math.Max(Zoom, 1);
        var picture = new Rect(0, 0, PictureWidth * zoom, PictureHeight * zoom);
        if (picture.IsEmpty || Frame is null)
        {
            return;
        }

        if (ShowTransparency)
        {
            drawingContext.DrawRectangle(Checkerboard, null, picture);
        }

        drawingContext.DrawImage(Frame, picture);
        if (ShowWhiteSquare)
        {
            DrawWhiteSquare(drawingContext, picture, zoom);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (ShowWhiteSquare && CaptureMouse())
        {
            dragging = true;
            ReportDrag(e);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            ReportDrag(e);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragging)
        {
            dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture(MouseEventArgs e) => dragging = false;

    private static DependencyProperty Register(string name, Type type, object? defaultValue, FrameworkPropertyMetadataOptions options) =>
        DependencyProperty.Register(name, type, typeof(AnimationPreview), new FrameworkPropertyMetadata(defaultValue, options));

    private static Brush CreateCheckerboard()
    {
        var squares = new GeometryGroup();
        squares.Children.Add(new RectangleGeometry(new Rect(0, 0, CheckerSize, CheckerSize)));
        squares.Children.Add(new RectangleGeometry(new Rect(CheckerSize, CheckerSize, CheckerSize, CheckerSize)));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 2 * CheckerSize, 2 * CheckerSize))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), null, squares));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 2 * CheckerSize, 2 * CheckerSize),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>The 8 x 8 <c>DITHER</c> bitmap as an overlay: its black pixels black, its white pixels see-through (5.4 PATAND).</summary>
    private static BitmapSource CreateDitherBitmap()
    {
        Rgba32Image dither = HeritageArt.Dither;
        uint[] pixels = [.. dither.Pixels.Select(p => (p & 0xFFFFFF) == 0 ? 0xFF000000 : 0u)];
        return PixelBitmaps.ToBitmap(new Rgba32Image(dither.Width, dither.Height, pixels));
    }

    private void DrawWhiteSquare(DrawingContext drawingContext, Rect picture, int zoom)
    {
        RectI window = WhiteSquare;
        var square = new Rect(window.Left * zoom, window.Top * zoom, window.Width * zoom, window.Height * zoom);
        var dither = new ImageBrush(DitherBitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, DitherBitmap.PixelWidth * zoom, DitherBitmap.PixelHeight * zoom),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        RenderOptions.SetBitmapScalingMode(dither, BitmapScalingMode.NearestNeighbor);
        dither.Freeze();
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(picture), new RectangleGeometry(square));
        drawingContext.DrawGeometry(dither, null, outside);

        var black = new Pen(Brushes.Black, 1);
        var white = new Pen(Brushes.White, 1);
        black.Freeze();
        white.Freeze();
        drawingContext.DrawRectangle(null, black, new Rect(square.X + 0.5, square.Y + 0.5, Math.Max(square.Width - 1, 0), Math.Max(square.Height - 1, 0)));
        drawingContext.DrawRectangle(null, white, new Rect(square.X - 0.5, square.Y - 0.5, square.Width + 1, square.Height + 1));
    }

    private void ReportDrag(MouseEventArgs e)
    {
        Point position = e.GetPosition(this);
        int zoom = Math.Max(Zoom, 1);
        SquareDragged?.Invoke(this, new Point(position.X / zoom, position.Y / zoom));
    }
}
