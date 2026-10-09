using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Rendering;

/// <summary>
/// Draws one display of the saver into a CPU frame at any zoom, in 5.4 order (background colour, picture, stamped piles,
/// animation, bulbs with glow, snow on the bulbs, message): the Settings preview and its enlarged corner, the <c>/p</c>
/// preview in Windows' dialog and frame dumps. Art is scaled for the zoom (area-averaged when small). The frame can cover
/// the whole display or only a part of it (a viewport), so a corner can be shown enlarged without drawing the rest.
/// </summary>
/// <remarks>Create it on an STA thread (the message is drawn with WPF text). Not thread-safe.</remarks>
internal sealed class CpuSaverRenderer
{
    private readonly SaverScene scene;
    private readonly ICpuCompositor compositor;
    private readonly ISpriteProvider bulbSprites;
    private readonly IBulbResolver bulbs;
    private readonly SaverSpriteCache sprites;
    private readonly Dictionary<SpriteArt, PremultipliedImage?[]> scaledFrames = new(ReferenceEqualityComparer.Instance);
    private readonly double zoom;
    private readonly SaverPixels pixels;
    private readonly PointI origin;
    private readonly PremultipliedImage backdrop;
    private readonly PremultipliedImage? message;
    private readonly PremultipliedImage? stamps;
    private readonly int stampsTop;
    private PremultipliedImage? snow;

    /// <summary>Prepares the renderer: draws the background colour and the picture once, and the message for the zoom.</summary>
    /// <param name="scene">The display's scene.</param>
    /// <param name="zoom">Target pixels per physical display pixel (1 = full size).</param>
    /// <param name="compositor">The CPU compositor.</param>
    /// <param name="bulbSprites">Bulb sprites and glow.</param>
    /// <param name="bulbs">Resolves the bulbs.</param>
    /// <param name="sprites">The animation sprite cache (may be shared by renderers of one run).</param>
    /// <param name="viewport">
    /// The part of the zoomed display to draw, in target pixels with the display's top-left at 0, 0 (clipped to the display),
    /// or null for the whole display.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The viewport does not overlap the display.</exception>
    public CpuSaverRenderer(
        SaverScene scene, double zoom, ICpuCompositor compositor, ISpriteProvider bulbSprites, IBulbResolver bulbs, SaverSpriteCache sprites,
        RectI? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(bulbSprites);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);
        this.scene = scene;
        this.compositor = compositor;
        this.bulbSprites = bulbSprites;
        this.bulbs = bulbs;
        this.sprites = sprites;
        this.zoom = zoom;
        pixels = new SaverPixels(scene.Plan.Scale * zoom, scene.Options.SmoothMotion);
        RectI bounds = scene.Plan.Display.Bounds;
        var whole = new RectI(
            0, 0,
            Math.Max(1, (int)Math.Round(bounds.Width * zoom, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(bounds.Height * zoom, MidpointRounding.AwayFromZero)));
        RectI view = viewport is { } part ? part.Intersect(whole) : whole;
        if (view.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "The viewport must overlap the display.");
        }

        origin = view.TopLeft;
        Viewport = view;
        Frame = new PremultipliedImage(view.Width, view.Height);
        backdrop = CreateBackdrop(scene, pixels, view, compositor);
        message = scene.Message is { } text ? SaverText.Render(text, pixels.Scale) : null;
        if (scene.StampReach > 0)
        {
            int top = Math.Clamp(pixels.Snap(scene.Field.Height - scene.StampReach), 0, whole.Height - 1) - origin.Y;
            stampsTop = Math.Max(0, top);
            if (stampsTop < Frame.Height)
            {
                stamps = new PremultipliedImage(Frame.Width, Frame.Height - stampsTop);
            }
        }
    }

    /// <summary>The frame (the viewport of the display at the zoom); <see cref="Render"/> redraws it.</summary>
    public PremultipliedImage Frame { get; }

    /// <summary>The part of the zoomed display the frame shows (target pixels, the display's top-left at 0, 0).</summary>
    public RectI Viewport { get; }

    /// <summary>The scene.</summary>
    public SaverScene Scene => scene;

    /// <summary>Keeps what the last simulation step stamped into the background and the snow that stuck to the bulbs.</summary>
    public void ApplyStep()
    {
        SaverSimulation simulation = scene.Simulation;
        if (stamps is not null)
        {
            foreach (SpriteStamp stamp in simulation.NewStamps)
            {
                compositor.Draw(stamps, Scaled(stamp.Art, stamp.Frame), pixels.Position(stamp.X, stamp.X, 1) - origin.X,
                    pixels.Position(stamp.Y, stamp.Y, 1) - origin.Y - stampsTop, 1f, CompositeMode.SourceOver);
            }
        }

        foreach (SnowCell cell in simulation.NewSnowCells)
        {
            RectI rect = SnowRect(cell).Offset(-origin.X, -origin.Y);
            if (rect.IntersectsWith(new RectI(0, 0, Frame.Width, Frame.Height)))
            {
                snow ??= new PremultipliedImage(Frame.Width, Frame.Height);
                compositor.Fill(snow, rect, cell.Color);
            }
        }
    }

    /// <summary>Draws the current state.</summary>
    /// <param name="progress">How far into the current step (Smooth Motion; 0-1).</param>
    public void Render(float progress)
    {
        backdrop.Pixels.AsSpan().CopyTo(Frame.Pixels);
        if (stamps is not null)
        {
            compositor.Draw(Frame, stamps, 0, stampsTop, 1f, CompositeMode.SourceOver);
        }

        IReadOnlyList<SpriteDraw> drawn = scene.Simulation.Sprites;
        for (int i = 0; i < drawn.Count; i++)
        {
            SpriteDraw sprite = drawn[i];
            PremultipliedImage image = Scaled(sprite.Art, sprite.Frame);
            int x = pixels.Position(sprite.PreviousX, sprite.X, progress) - origin.X;
            int y = pixels.Position(sprite.PreviousY, sprite.Y, progress) - origin.Y;
            if (x < Frame.Width && y < Frame.Height && x + image.Width > 0 && y + image.Height > 0)
            {
                compositor.Draw(Frame, image, x, y, 1f, CompositeMode.SourceOver);
            }
        }

        DrawBulbs();
        if (snow is not null)
        {
            compositor.Draw(Frame, snow, 0, 0, 1f, CompositeMode.SourceOver);
        }

        if (message is not null && scene.Simulation.Message is { } bounce && scene.Message is { } text)
        {
            int left = pixels.Snap(text.Range.Left - SaverText.ShadowOffset) - origin.X;
            int top = pixels.Position(bounce.PreviousTop - SaverText.ShadowOffset, bounce.Top - SaverText.ShadowOffset, progress) - origin.Y;
            compositor.Draw(Frame, message, left, top, 1f, CompositeMode.SourceOver);
        }
    }

    /// <summary>The background colour with the part of the picture inside the viewport, drawn once.</summary>
    private static PremultipliedImage CreateBackdrop(SaverScene scene, SaverPixels pixels, RectI view, ICpuCompositor compositor)
    {
        var image = new PremultipliedImage(view.Width, view.Height);
        compositor.Clear(image, scene.Background.ToBgra32());
        if (scene.Picture is { } picture && PictureLayer.Compose(picture, pixels, view, scene.Options.Pixels, compositor) is { } layer)
        {
            compositor.Draw(image, layer.Image, layer.At.X - view.Left, layer.At.Y - view.Top, 1f, CompositeMode.SourceOver);
        }

        return image;
    }

    /// <summary>A frame scaled for this renderer, looked up by reference (the shared cache is asked once per frame).</summary>
    private PremultipliedImage Scaled(SpriteArt art, int frame)
    {
        if (!scaledFrames.TryGetValue(art, out PremultipliedImage?[]? frames))
        {
            frames = new PremultipliedImage?[art.FrameCount];
            scaledFrames[art] = frames;
        }

        int index = ((frame % frames.Length) + frames.Length) % frames.Length;
        return frames[index] ??= sprites.Get(art, index, pixels.Scale, scene.Options.Pixels);
    }

    private void DrawBulbs()
    {
        if (scene.Bulbs is not { } saverBulbs)
        {
            return;
        }

        RectI bounds = scene.Plan.Display.Bounds;
        compositor.DrawLights(Frame, new LightsRenderRequest
        {
            Layout = saverBulbs.Layout,
            DisplayId = scene.Plan.Display.DeviceId,
            States = saverBulbs.States,
            Bulbs = bulbs,
            Sprites = bulbSprites,
            Zoom = zoom,
            OffsetX = -bounds.Left * zoom - origin.X,
            OffsetY = -bounds.Top * zoom - origin.Y,
            Style = scene.Options.Pixels,
            GlowIntensity = scene.Options.GlowIntensity,
        });
    }

    /// <summary>One DIP of snow, at least one pixel (zoomed-display pixels).</summary>
    private RectI SnowRect(SnowCell cell)
    {
        RectI rect = pixels.Snap(RectI.FromXYWH(cell.X, cell.Y, 1, 1));
        return new RectI(rect.Left, rect.Top, Math.Max(rect.Right, rect.Left + 1), Math.Max(rect.Bottom, rect.Top + 1));
    }
}
