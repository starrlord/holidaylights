using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Core.Imaging;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// An animation as Bulb Editing shows it: the frames of the faithful 5.4 decoder as frozen bitmaps (what the desktop
/// draws), and whether a standard GIF viewer would show something else (the GIF tip, PRODUCT-SPEC 3.3.1).
/// </summary>
/// <remarks>Immutable; created on the thread pool; the bitmaps are frozen, so any thread may use them.</remarks>
internal sealed class EditorAnimation
{
    private EditorAnimation(BulbGif gif, IReadOnlyList<Rgba32Image> frames, bool isDamaged, bool differsFromStandard)
    {
        Gif = gif;
        Pictures = frames;
        Frames = [.. frames.Select(PixelBitmaps.ToBitmap)];
        IsDamaged = isDamaged;
        DiffersFromStandard = differsFromStandard;
    }

    /// <summary>The GIF.</summary>
    public BulbGif Gif { get; }

    /// <summary>The decoded frames (straight alpha).</summary>
    public IReadOnlyList<Rgba32Image> Pictures { get; }

    /// <summary>The frames as bitmaps.</summary>
    public IReadOnlyList<BitmapSource> Frames { get; }

    /// <summary>The frame size in art pixels.</summary>
    public SizeI Size => Pictures[0].Size;

    /// <summary>True when the faithful decoder rejects the GIF: the 5.4 WARNING picture stands in.</summary>
    public bool IsDamaged { get; }

    /// <summary>True when a standard GIF decoder shows different frames (holes or colours), so the GIF tip applies.</summary>
    public bool DiffersFromStandard { get; }

    /// <summary>Decodes a GIF (any thread).</summary>
    /// <param name="gif">The GIF.</param>
    /// <returns>The animation.</returns>
    public static EditorAnimation Decode(BulbGif gif)
    {
        GifAnimation classic;
        try
        {
            classic = GifDecoder.DecodeClassic(gif.Bytes.Span);
        }
        catch (InvalidDataException)
        {
            return new EditorAnimation(gif, [HeritageArt.Warning], isDamaged: true, differsFromStandard: false);
        }

        return new EditorAnimation(gif, classic.Frames, isDamaged: false, StandardDecodeDiffers(gif, classic));
    }

    /// <summary>The 32 x 32 list picture of frame 0 (the Bulb List and slot-map view of the preview slot).</summary>
    /// <param name="window">The list-preview window.</param>
    /// <returns>The cropped bitmap, or the whole frame when the window is empty or outside it.</returns>
    public BitmapSource ListPicture(RectI window)
    {
        Rgba32Image first = Pictures[0];
        bool inside = !window.IsEmpty && window.Left >= 0 && window.Top >= 0 && window.Right <= first.Width && window.Bottom <= first.Height;
        return inside ? PixelBitmaps.ToBitmap(first.Crop(window)) : Frames[0];
    }

    private static bool StandardDecodeDiffers(BulbGif gif, GifAnimation classic)
    {
        GifAnimation standard;
        try
        {
            standard = GifDecoder.DecodeStandard(gif.Bytes.Span);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return standard.Frames.Count != classic.Frames.Count
            || standard.Frames.Zip(classic.Frames).Any(pair => !pair.First.Pixels.AsSpan().SequenceEqual(pair.Second.Pixels));
    }
}

/// <summary>Converts decoded pictures to WPF bitmaps.</summary>
internal static class PixelBitmaps
{
    /// <summary>Wraps a straight-alpha picture as a frozen BGRA bitmap at 96 DPI (one art pixel per DIP).</summary>
    /// <param name="image">The picture.</param>
    /// <returns>The bitmap.</returns>
    public static BitmapSource ToBitmap(Rgba32Image image)
    {
        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>Decodes each GIF of an editing session once, on the thread pool.</summary>
internal sealed class AnimationCache
{
    private readonly Dictionary<BulbGif, Task<EditorAnimation>> animations = [];

    /// <summary>The animation of a GIF (decoded on first request).</summary>
    /// <param name="gif">The GIF.</param>
    /// <returns>A task with the animation.</returns>
    public Task<EditorAnimation> GetAsync(BulbGif gif)
    {
        if (!animations.TryGetValue(gif, out Task<EditorAnimation>? task))
        {
            task = Task.Run(() => EditorAnimation.Decode(gif));
            animations.Add(gif, task);
        }

        return task;
    }
}
