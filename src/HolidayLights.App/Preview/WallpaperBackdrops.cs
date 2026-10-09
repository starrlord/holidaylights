using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HolidayLights.App.Preview;

/// <summary>
/// The backdrop of a display in a light stage (PRODUCT-SPEC 3.0.2): its wallpaper with its position mode (a slideshow
/// shows its current image), else its background colour, else nothing (the stage then paints the night gradient).
/// Pictures are decoded and scaled on the thread pool at the stage's pixel size and cached.
/// </summary>
public sealed class WallpaperBackdrops
{
    private const int CacheSize = 24;
    private const string LogSource = "Settings.Preview";

    private readonly object gate = new();
    private readonly LinkedList<(BackdropKey Key, PremultipliedImage? Image)> cache = new();
    private readonly Dictionary<BackdropKey, Task<PremultipliedImage?>> pending = [];

    /// <summary>The cache shared by every stage of the process.</summary>
    public static WallpaperBackdrops Shared { get; } = new();

    /// <summary>Drops every cached backdrop (the Settings window closed; review r1 #19); pending decodes still finish.</summary>
    public void Clear()
    {
        lock (gate)
        {
            cache.Clear();
        }
    }

    /// <summary>Returns the backdrop of a display at a size, decoding it on the thread pool when it is not cached.</summary>
    /// <param name="provider">The wallpaper provider.</param>
    /// <param name="display">The display.</param>
    /// <param name="desktop">The bounding box of every display (for "Span").</param>
    /// <param name="size">The size of the display in the stage, in pixels.</param>
    /// <param name="log">The log.</param>
    /// <returns>The backdrop (opaque, premultiplied), or null when the night gradient should show.</returns>
    public Task<PremultipliedImage?> GetAsync(IWallpaperProvider provider, DisplayInfo display, RectI desktop, SizeI size, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(log);
        if (size.IsEmpty)
        {
            return Task.FromResult<PremultipliedImage?>(null);
        }

        return Task.Run(() =>
        {
            WallpaperInfo info = provider.GetWallpaper(display);
            var key = new BackdropKey(display.Bounds, desktop, size, info.ImagePath, StampOf(info.ImagePath), info.Position, info.BackgroundColor);
            Task<PremultipliedImage?> work;
            lock (gate)
            {
                foreach ((BackdropKey cachedKey, PremultipliedImage? image) in cache)
                {
                    if (cachedKey == key)
                    {
                        return Task.FromResult(image);
                    }
                }

                if (!pending.TryGetValue(key, out work!))
                {
                    work = Task.Run(() => Compose(key, log));
                    pending[key] = work;
                }
            }

            return Remember(key, work);
        });
    }

    private async Task<PremultipliedImage?> Remember(BackdropKey key, Task<PremultipliedImage?> work)
    {
        PremultipliedImage? image = await work.ConfigureAwait(false);
        lock (gate)
        {
            pending.Remove(key);
            if (!cache.Any(c => c.Key == key))
            {
                cache.AddFirst((key, image));
                while (cache.Count > CacheSize)
                {
                    cache.RemoveLast();
                }
            }
        }

        return image;
    }

    private static DateTime StampOf(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
    }

    private static PremultipliedImage? Compose(BackdropKey key, IAppLog log)
    {
        if (key.ImagePath is null && key.Background is null)
        {
            return null;
        }

        var target = new PremultipliedImage(key.Size.Width, key.Size.Height);
        var all = new RectI(0, 0, key.Size.Width, key.Size.Height);
        PixelBuffers.Fill(target, all, key.Background ?? RgbColor.Black);
        if (key.ImagePath is null || key.Stamp == default)
        {
            return target;
        }

        try
        {
            DrawPicture(target, key);
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException)
        {
            log.Warn(LogSource, "The wallpaper could not be drawn in a preview.", e);
            return key.Background is null ? null : target;
        }
    }

    private static void DrawPicture(PremultipliedImage target, BackdropKey key)
    {
        BitmapFrame header = BitmapFrame.Create(new Uri(key.ImagePath!), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        int imageWidth = header.PixelWidth;
        int imageHeight = header.PixelHeight;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        RectI display = key.Bounds;
        double zoom = (double)key.Size.Width / display.Width;
        var clip = new RectI(0, 0, target.Width, target.Height);

        if (key.Position == WallpaperPosition.Tile)
        {
            (uint[] tile, int tileWidth, int tileHeight) = Decode(key.ImagePath!, Math.Max(1, (int)Math.Round(imageWidth * zoom)), Math.Max(1, (int)Math.Round(imageHeight * zoom)));
            for (int y = 0; y < target.Height; y += tileHeight)
            {
                for (int x = 0; x < target.Width; x += tileWidth)
                {
                    PixelBuffers.Blit(target, tile, tileWidth, tileHeight, x, y, clip);
                }
            }

            return;
        }

        // The picture's rectangle in physical pixels relative to the display's top-left corner.
        (double left, double top, double width, double height) = Placement(key.Position, imageWidth, imageHeight, display, key.Desktop);
        int pixelWidth = Math.Max(1, (int)Math.Round(width * zoom));
        int pixelHeight = Math.Max(1, (int)Math.Round(height * zoom));
        (uint[] pixels, int decodedWidth, int decodedHeight) = Decode(key.ImagePath!, pixelWidth, pixelHeight);
        PixelBuffers.Blit(target, pixels, decodedWidth, decodedHeight, (int)Math.Round(left * zoom), (int)Math.Round(top * zoom), clip);
    }

    private static (double Left, double Top, double Width, double Height) Placement(WallpaperPosition position, int imageWidth, int imageHeight, RectI display, RectI desktop)
    {
        double w = display.Width;
        double h = display.Height;
        switch (position)
        {
            case WallpaperPosition.Center:
                return ((w - imageWidth) / 2, (h - imageHeight) / 2, imageWidth, imageHeight);
            case WallpaperPosition.Stretch:
                return (0, 0, w, h);
            case WallpaperPosition.Fit:
            {
                double scale = Math.Min(w / imageWidth, h / imageHeight);
                return ((w - imageWidth * scale) / 2, (h - imageHeight * scale) / 2, imageWidth * scale, imageHeight * scale);
            }

            case WallpaperPosition.Span when !desktop.IsEmpty:
            {
                double scale = Math.Max((double)desktop.Width / imageWidth, (double)desktop.Height / imageHeight);
                double left = desktop.Left + (desktop.Width - imageWidth * scale) / 2 - display.Left;
                double top = desktop.Top + (desktop.Height - imageHeight * scale) / 2 - display.Top;
                return (left, top, imageWidth * scale, imageHeight * scale);
            }

            default:
            {
                double scale = Math.Max(w / imageWidth, h / imageHeight);
                return ((w - imageWidth * scale) / 2, (h - imageHeight * scale) / 2, imageWidth * scale, imageHeight * scale);
            }
        }
    }

    private static (uint[] Pixels, int Width, int Height) Decode(string path, int width, int height)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = width;
        image.DecodePixelHeight = height;
        image.EndInit();
        image.Freeze();

        // Wallpapers are opaque: drop any alpha (Bgr32) and mark every pixel opaque.
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        var pixels = new uint[w * h];
        converted.CopyPixels(pixels, w * 4, 0);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] |= 0xFF000000;
        }

        return (pixels, w, h);
    }

    private readonly record struct BackdropKey(
        RectI Bounds, RectI Desktop, SizeI Size, string? ImagePath, DateTime Stamp, WallpaperPosition Position, RgbColor? Background);
}
