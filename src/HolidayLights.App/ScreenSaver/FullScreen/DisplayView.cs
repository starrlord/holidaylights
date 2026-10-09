using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;
using Vortice.DirectComposition;
using CompositeMode = HolidayLights.Core.Abstractions.CompositeMode;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// One display of the full-screen saver as a DirectComposition tree on its window, in 5.4 drawing order: picture, stamped
/// piles, animation sprites, glow, bulbs, snow on the bulbs, message (the background colour is the window's). A frame only
/// moves sprite visuals and the message and changes bulb opacities; stamps and snow are written into virtual surfaces that
/// allocate memory only where something was drawn.
/// </summary>
/// <remarks>Physical pixels of the display. Saver UI thread.</remarks>
internal sealed class DisplayView : IDisposable
{
    private const string LogSource = "ScreenSaver";

    private readonly SaverScene scene;
    private readonly SaverRenderServices services;
    private readonly SaverCompositor compositor;
    private readonly SaverPixels pixels;
    private readonly float glowIntensity;
    private readonly List<IDisposable> owned = [];
    private readonly IDCompositionTarget target;
    private readonly IDCompositionVisual sprites;
    private readonly List<SpriteVisual> spritePool = [];
    private readonly Dictionary<SpriteArt, PremultipliedImage?[]> scaledFrames = new(ReferenceEqualityComparer.Instance);
    private readonly BulbVisuals? bulbs;
    private readonly IDCompositionVisual? message;
    private readonly PremultipliedImage? stampPixels;
    private readonly IDCompositionVirtualSurface? stampSurface;
    private readonly int stampsTop;
    private readonly IDCompositionVirtualSurface? snowSurface;
    private readonly Dictionary<uint, PremultipliedImage> snowColors = [];
    private readonly IDCompositionVisual root;

    /// <summary>Builds the tree of a scene on a window.</summary>
    /// <param name="scene">The display's scene.</param>
    /// <param name="services">Sprites and the CPU compositor (stamps).</param>
    /// <param name="compositor">The devices.</param>
    /// <param name="hwnd">The display's saver window.</param>
    public DisplayView(SaverScene scene, SaverRenderServices services, SaverCompositor compositor, nint hwnd)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(compositor);
        this.scene = scene;
        this.services = services;
        this.compositor = compositor;
        pixels = new SaverPixels(scene.Plan.Scale, scene.Options.SmoothMotion);
        RectI bounds = scene.Plan.Display.Bounds;

        // Half the additive glow on a display in HDR, like the desktop lights (PO decision 2; review r1 #43).
        glowIntensity = scene.Options.GlowIntensity * HdrGlow.ScaleFor(bounds);
        target = Own(compositor.CreateTarget(hwnd));
        root = Own(compositor.CreateVisual());

        AddPicture();
        if (scene.StampReach > 0)
        {
            stampsTop = Math.Clamp(pixels.Snap(scene.Field.Height - scene.StampReach), 0, bounds.Height - 1);
            stampPixels = new PremultipliedImage(bounds.Width, bounds.Height - stampsTop);
            stampSurface = Own(compositor.CreateVirtualSurface(stampPixels.Width, stampPixels.Height));
            SaverCompositor.Append(root, Own(compositor.CreateVisual(0, stampsTop, stampSurface)));
        }

        sprites = Own(compositor.CreateVisual());
        SaverCompositor.Append(root, sprites);
        if (scene.Bulbs is { } saverBulbs)
        {
            bulbs = Own(new BulbVisuals(compositor, saverBulbs, scene.Plan.Display, services.Bulbs, services.BulbSprites, scene.Options.Pixels,
                glowIntensity));
            SaverCompositor.Append(root, bulbs.Glow);
            SaverCompositor.Append(root, bulbs.Bulbs);
            if (scene.Simulation.Module is SnowModule)
            {
                snowSurface = Own(compositor.CreateVirtualSurface(bounds.Width, bounds.Height));
                SaverCompositor.Append(root, Own(compositor.CreateVisual(0, 0, snowSurface)));
            }
        }

        if (scene.Message is { } text)
        {
            message = Own(compositor.CreateVisual(0, 0, compositor.GetSurface(SaverText.Render(text, pixels.Scale))));
            SaverCompositor.Append(root, message);
        }

        target.SetRoot(root).CheckError();
    }

    /// <summary>The scene.</summary>
    public SaverScene Scene => scene;

    /// <summary>Keeps what the last simulation step stamped into the background and the snow that stuck to the bulbs.</summary>
    public void ApplyStep()
    {
        SaverSimulation simulation = scene.Simulation;
        if (stampPixels is not null && stampSurface is not null)
        {
            foreach (SpriteStamp stamp in simulation.NewStamps)
            {
                Stamp(stamp, stampPixels, stampSurface);
            }
        }

        if (snowSurface is not null)
        {
            foreach (SnowCell cell in simulation.NewSnowCells)
            {
                AddSnow(cell, snowSurface);
            }
        }
    }

    /// <summary>Shows the current state.</summary>
    /// <param name="progress">How far into the current step (Smooth Motion).</param>
    /// <param name="moveSprites">True when the sprites and the message moved since the last frame.</param>
    public void RenderFrame(float progress, bool moveSprites)
    {
        if (moveSprites)
        {
            MoveSprites(progress);
            if (message is not null && scene.Simulation.Message is { } bounce && scene.Message is { } text)
            {
                message.SetOffsetX(pixels.Snap(text.Range.Left - SaverText.ShadowOffset));
                message.SetOffsetY(pixels.Position(bounce.PreviousTop - SaverText.ShadowOffset, bounce.Top - SaverText.ShadowOffset, progress));
            }
        }

        if (scene.Bulbs is { } saverBulbs)
        {
            bulbs?.Update(saverBulbs.States, glowIntensity);
        }
    }

    /// <summary>Releases the tree.</summary>
    public void Dispose()
    {
        target.SetRoot(null!);
        foreach (SpriteVisual sprite in spritePool)
        {
            sprite.Visual.Dispose();
        }

        foreach (IDisposable resource in owned)
        {
            resource.Dispose();
        }

        spritePool.Clear();
        owned.Clear();
    }

    /// <summary>Gives each drawn sprite a visual from the pool (new ones are added in front); unused ones show nothing.</summary>
    private void MoveSprites(float progress)
    {
        IReadOnlyList<SpriteDraw> drawn = scene.Simulation.Sprites;
        for (int i = 0; i < drawn.Count; i++)
        {
            if (i == spritePool.Count)
            {
                IDCompositionVisual visual = compositor.CreateVisual();
                SaverCompositor.Append(sprites, visual);
                spritePool.Add(new SpriteVisual(visual));
            }

            SpriteDraw sprite = drawn[i];
            spritePool[i].Show(compositor, Scaled(sprite.Art, sprite.Frame),
                pixels.Position(sprite.PreviousX, sprite.X, progress), pixels.Position(sprite.PreviousY, sprite.Y, progress));
        }

        for (int i = drawn.Count; i < spritePool.Count; i++)
        {
            spritePool[i].Hide();
        }
    }

    /// <summary>A frame scaled for this display, looked up by reference (the shared cache is asked once per frame).</summary>
    private PremultipliedImage Scaled(SpriteArt art, int frame)
    {
        if (!scaledFrames.TryGetValue(art, out PremultipliedImage?[]? frames))
        {
            frames = new PremultipliedImage?[art.FrameCount];
            scaledFrames[art] = frames;
        }

        return frames[frame] ??= services.Sprites.Get(art, frame, pixels.Scale, scene.Options.Pixels);
    }

    /// <summary>
    /// The picture as one layer of the part on screen (one surface no larger than the display, whatever the picture's size
    /// or the number of tiles). A picture that still cannot be shown is left out and the saver runs without it, as 5.4 did
    /// with any picture failure; the bulbs and the animation are never lost to it.
    /// </summary>
    private void AddPicture()
    {
        if (scene.Picture is not { } picture)
        {
            return;
        }

        try
        {
            RectI bounds = scene.Plan.Display.Bounds;
            if (PictureLayer.Compose(picture, pixels, new RectI(0, 0, bounds.Width, bounds.Height), scene.Options.Pixels, services.Compositor) is { } layer)
            {
                IDCompositionSurface? surface = compositor.GetSurface(layer.Image);
                SaverCompositor.Append(root, Own(compositor.CreateVisual(layer.At.X, layer.At.Y, surface)));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            services.Log.Error(LogSource, "The screen saver picture couldn't be shown; the saver runs without it.", ex);
        }
    }

    private void Stamp(SpriteStamp stamp, PremultipliedImage layer, IDCompositionVirtualSurface surface)
    {
        PremultipliedImage image = Scaled(stamp.Art, stamp.Frame);
        int x = pixels.Position(stamp.X, stamp.X, 1);
        int y = pixels.Position(stamp.Y, stamp.Y, 1) - stampsTop;
        services.Compositor.Draw(layer, image, x, y, 1f, CompositeMode.SourceOver);
        RectI dirty = RectI.FromXYWH(x, y, image.Width, image.Height).Intersect(new RectI(0, 0, layer.Width, layer.Height));
        compositor.Upload(surface, dirty, layer, dirty);
    }

    /// <summary>One DIP of snow on a bulb, drawn into the virtual surface above the bulbs.</summary>
    private void AddSnow(SnowCell cell, IDCompositionVirtualSurface surface)
    {
        RectI bounds = scene.Plan.Display.Bounds;
        RectI rect = pixels.Snap(RectI.FromXYWH(cell.X, cell.Y, 1, 1));
        rect = new RectI(rect.Left, rect.Top, Math.Max(rect.Right, rect.Left + 1), Math.Max(rect.Bottom, rect.Top + 1))
            .Intersect(new RectI(0, 0, bounds.Width, bounds.Height));
        if (rect.IsEmpty)
        {
            return;
        }

        if (!snowColors.TryGetValue(cell.Color, out PremultipliedImage? block))
        {
            int size = (int)Math.Ceiling(pixels.Scale) + 1;
            block = new PremultipliedImage(size, size, Enumerable.Repeat(cell.Color, size * size).ToArray());
            snowColors[cell.Color] = block;
        }

        compositor.Upload(surface, rect, block, new RectI(0, 0, rect.Width, rect.Height));
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    /// <summary>A pooled sprite visual and what it shows now (only changes are sent).</summary>
    private sealed class SpriteVisual(IDCompositionVisual visual)
    {
        private PremultipliedImage? shown;
        private int x = int.MinValue;
        private int y = int.MinValue;

        public IDCompositionVisual Visual => visual;

        public void Show(SaverCompositor compositor, PremultipliedImage image, int left, int top)
        {
            if (!ReferenceEquals(image, shown))
            {
                shown = image;
                visual.SetContent(compositor.GetSurface(image)!);
            }

            if (left != x)
            {
                x = left;
                visual.SetOffsetX(left);
            }

            if (top != y)
            {
                y = top;
                visual.SetOffsetY(top);
            }
        }

        public void Hide()
        {
            if (shown is not null)
            {
                shown = null;
                visual.SetContent(null!);
            }
        }
    }
}

/// <summary>What the saver views draw with.</summary>
/// <param name="Bulbs">Resolves bulbs.</param>
/// <param name="BulbSprites">Bulb sprites and glow.</param>
/// <param name="Compositor">The CPU compositor (stamped piles, the picture layer).</param>
/// <param name="Sprites">The animation sprite cache.</param>
/// <param name="Log">The log (a picture that cannot be shown).</param>
internal sealed record SaverRenderServices(IBulbResolver Bulbs, ISpriteProvider BulbSprites, ICpuCompositor Compositor, SaverSpriteCache Sprites, IAppLog Log);
