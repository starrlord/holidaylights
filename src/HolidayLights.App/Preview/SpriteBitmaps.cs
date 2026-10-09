using System.Windows;
using System.Windows.Media.Imaging;

namespace HolidayLights.App.Preview;

/// <summary>
/// Frozen WPF bitmaps of scaled bulb sprites for thumbnails (chips, corners, list rows), shared by every thumbnail and
/// kept for the most recently used sprites. Scaling itself is the sprite provider's (MMPX or Crisp, PRODUCT-SPEC 5.3.3).
/// </summary>
public static class SpriteBitmaps
{
    private const int Capacity = 768;

    private static readonly Dictionary<Key, LinkedListNode<(Key Key, BitmapSource Bitmap)>> Index = [];
    private static readonly LinkedList<(Key Key, BitmapSource Bitmap)> Order = new();
    private static readonly object Gate = new();

    /// <summary>Drops every bitmap (the Settings window closed; review r1 #19).</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Index.Clear();
            Order.Clear();
        }
    }

    /// <summary>Returns a frame of a bulb scaled by <paramref name="scale"/>, optionally cropped to a window of the art.</summary>
    /// <param name="sprites">The sprite provider.</param>
    /// <param name="bulb">The bulb.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">The flavor.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="scale">Device pixels per art pixel.</param>
    /// <param name="style">Smooth or Crisp.</param>
    /// <param name="dpi">The DPI of the element that shows it.</param>
    /// <param name="crop">A window of the art in art pixels (the 32 x 32 list preview), or null for the whole cell.</param>
    /// <returns>The frozen bitmap, or null when the crop is empty.</returns>
    public static BitmapSource? Get(ISpriteProvider sprites, IBulb bulb, CellSlot slot, int flavor, int frame, double scale, SpriteStyle style, DpiScale dpi, RectI? crop = null)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(bulb);
        var key = new Key(bulb.ContentKey, slot, flavor, frame, Math.Round(scale, 4), style, dpi.PixelsPerDip, crop);
        lock (Gate)
        {
            if (Index.TryGetValue(key, out LinkedListNode<(Key Key, BitmapSource Bitmap)>? node))
            {
                Order.Remove(node);
                Order.AddFirst(node);
                return node.Value.Bitmap;
            }
        }

        PremultipliedImage image = sprites.GetSprite(bulb, slot, flavor, frame, scale, style);
        if (crop is { } window)
        {
            RectI area = new RectI(
                    (int)Math.Round(window.Left * scale), (int)Math.Round(window.Top * scale),
                    (int)Math.Round(window.Right * scale), (int)Math.Round(window.Bottom * scale))
                .Intersect(new RectI(0, 0, image.Width, image.Height));
            if (area.IsEmpty)
            {
                return null;
            }

            image = image.Crop(area);
        }

        BitmapSource bitmap = PixelBuffers.ToFrozenBitmap(image, dpi);
        lock (Gate)
        {
            if (!Index.ContainsKey(key))
            {
                Index[key] = Order.AddFirst((key, bitmap));
                while (Order.Count > Capacity && Order.Last is { } last)
                {
                    Index.Remove(last.Value.Key);
                    Order.RemoveLast();
                }
            }
        }

        return bitmap;
    }

    private readonly record struct Key(string ContentKey, CellSlot Slot, int Flavor, int Frame, double Scale, SpriteStyle Style, double PixelsPerDip, RectI? Crop);
}
