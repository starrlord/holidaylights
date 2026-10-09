using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings;

/// <summary>
/// The pictures of the screen saver animation tiles (PRODUCT-SPEC 3.5.5): the 5.4 art of each animation on a night well -
/// the built-in bulb a floater animation uses (animated), the first frame of a GIF floater, the snow, flakes and balloons
/// of the three special modules, or an add-on bulb chosen as the animation.
/// </summary>
public static class SaverAnimationArt
{
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Table = new(LoadTable);
    private static readonly Dictionary<string, BitmapSource?> Bitmaps = new(StringComparer.Ordinal);

    /// <summary>Creates the art of a tile.</summary>
    /// <param name="animation">The animation value ("Snow", "Angel", <c>bulb:&lt;id&gt;</c>).</param>
    /// <param name="services">The services (bulb art).</param>
    /// <returns>The art, sized to its tile by the caller.</returns>
    public static FrameworkElement Create(string animation, IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(services);
        if (SaverAnimations.TryGetBulbId(animation, out string? bulbId))
        {
            return Bulb(services, bulbId);
        }

        switch (animation)
        {
            case SaverAnimations.None:
                return NoneText();
            case SaverAnimations.Snow:
                return Snow();
            case SaverAnimations.SnowFlakes:
                return Sprites(HeritageAssets.Flake, HeritageAssets.FlakeMask, 9, 5, [(10, 12, 0), (44, 8, 2), (26, 34, 1), (58, 40, 3), (14, 56, 4), (48, 62, 0)], 2);
            case SaverAnimations.Balloons:
                return Sprites(HeritageAssets.Balloon, HeritageAssets.BalloonMask, 21, 6, [(6, 10, 0), (32, 4, 2), (58, 14, 4)], 1);
        }

        if (!Table.Value.TryGetValue(animation, out int source))
        {
            return new Border();
        }

        if (source < 0)
        {
            return new Image { Source = Load(HeritageAssets.SaverGif(-source)), Stretch = Stretch.Uniform, Margin = new Thickness(6) };
        }

        string? builtIn = services.Bulbs.All.FirstOrDefault(b => b.Origin == BulbOrigin.BuiltIn && services.Bulbs.TryGetBulb(b.Id, out IBulb? bulb) && bulb.LegacyId == source)?.Id;
        return builtIn is null ? new Border() : Bulb(services, builtIn);
    }

    /// <summary>"(None)" in the secondary text colour of night wells (the system GrayText in High Contrast).</summary>
    /// <returns>The text.</returns>
    public static TextBlock NoneText()
    {
        var text = new TextBlock { Text = SaverAnimations.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, Styles.ThemeKeys.OnNightWellSecondaryBrush);
        return text;
    }

    private static BulbThumbnail Bulb(IAppServices services, string bulbId) => new()
    {
        Services = services,
        BulbId = bulbId,
        ShowListPreview = true,
        IsAnimated = true,
        MaxArtScale = 2,
        Margin = new Thickness(8),
    };

    /// <summary>The animation name to its source: the built-in bulb id (records) or the negative GIF resource id.</summary>
    private static Dictionary<string, int> LoadTable()
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        using Stream stream = EmbeddedAssets.Open(HeritageAssets.SaverAnimationTable);
        using JsonDocument document = JsonDocument.Parse(stream);
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString() ?? "";
            int count = entry.GetProperty("count").GetInt32();
            JsonElement records = entry.GetProperty("records");
            if (count < 1)
            {
                table[name] = count;
            }
            else if (records.GetArrayLength() > 0)
            {
                table[name] = records[0][0].GetInt32();
            }
        }

        return table;
    }

    private static Canvas Snow()
    {
        var canvas = new Canvas { Width = 72, Height = 72 };
        var random = new Random(5);
        for (int i = 0; i < 26; i++)
        {
            double size = 1 + random.Next(3);
            var flake = new Ellipse { Width = size, Height = size };
            flake.SetResourceReference(Shape.FillProperty, Styles.ThemeKeys.OnNightWellBrush);
            Canvas.SetLeft(flake, random.Next(70));
            Canvas.SetTop(flake, random.Next(70));
            canvas.Children.Add(flake);
        }

        return canvas;
    }

    /// <summary>Masked sprites from a 5.4 strip (white mask pixels are transparent) placed at fixed spots.</summary>
    private static Canvas Sprites(string image, string mask, int frameWidth, int frames, (int X, int Y, int Frame)[] spots, int scale)
    {
        var canvas = new Canvas { Width = 72, Height = 72 };
        if (Masked(image, mask) is not { } sheet)
        {
            return canvas;
        }

        foreach ((int x, int y, int frame) in spots)
        {
            var sprite = new Image
            {
                Source = new CroppedBitmap(sheet, new Int32Rect(frameWidth * (frame % frames), 0, frameWidth, sheet.PixelHeight)),
                Width = frameWidth * scale,
                Height = sheet.PixelHeight * scale,
            };
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(sprite, x);
            Canvas.SetTop(sprite, y);
            canvas.Children.Add(sprite);
        }

        return canvas;
    }

    private static BitmapSource? Masked(string image, string mask)
    {
        string key = image + "|" + mask;
        lock (Bitmaps)
        {
            if (Bitmaps.TryGetValue(key, out BitmapSource? cached))
            {
                return cached;
            }
        }

        BitmapSource? result = null;
        if (Load(image) is { } color && Load(mask) is { } alpha)
        {
            var colorPixels = new FormatConvertedBitmap(color, PixelFormats.Bgra32, null, 0);
            var maskPixels = new FormatConvertedBitmap(alpha, PixelFormats.Bgra32, null, 0);
            int width = colorPixels.PixelWidth;
            int height = colorPixels.PixelHeight;
            byte[] pixels = new byte[width * height * 4];
            byte[] maskBytes = new byte[width * height * 4];
            colorPixels.CopyPixels(pixels, width * 4, 0);
            maskPixels.CopyPixels(maskBytes, width * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                bool transparent = maskBytes[i] > 127;
                pixels[i + 3] = transparent ? (byte)0 : (byte)255;
                if (transparent)
                {
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                }
            }

            result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            result.Freeze();
        }

        lock (Bitmaps)
        {
            Bitmaps[key] = result;
        }

        return result;
    }

    private static BitmapSource? Load(string asset)
    {
        if (!EmbeddedAssets.Exists(asset))
        {
            return null;
        }

        using var stream = new MemoryStream(EmbeddedAssets.ReadAllBytes(asset));
        BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }
}
