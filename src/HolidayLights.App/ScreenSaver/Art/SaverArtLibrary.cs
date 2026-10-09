using System.Collections.Concurrent;
using System.IO;
using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Imaging;

namespace HolidayLights.App.ScreenSaver.Art;

/// <summary>
/// The pictures of the saver animations: the BALLOON strip, the floaters of the 21
/// picture animations (built-in bulb cells and the RCDATA GIFs 3100-3105) and an add-on bulb's animations. Built-in art is
/// decoded once per process; everything is straight-alpha art in art pixels (one per DIP).
/// </summary>
/// <remarks>Thread-safe.</remarks>
internal static class SaverArtLibrary
{
    /// <summary>Colours in the BALLOON strip: red, blue, orange, purple, yellow, green.</summary>
    public const int BalloonColors = 6;

    private static readonly Lazy<SpriteArt> BalloonArt = new(CreateBalloons);
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<SpriteArt>>> TableArt = new(StringComparer.Ordinal);

    /// <summary>The six 21 x 50 balloons; the frame is the colour.</summary>
    public static SpriteArt Balloons => BalloonArt.Value;

    /// <summary>
    /// The floater pictures of a table animation in 5.4 order: one strip per record (the record's cells as frames), or the
    /// whole GIF as one strip. A cell 5.4 could not resolve shows the WARNING picture.
    /// </summary>
    /// <param name="entry">The table entry.</param>
    /// <returns>The pictures (floater i uses picture i mod count).</returns>
    public static IReadOnlyList<SpriteArt> ForTableAnimation(SaverAnimationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return TableArt.GetOrAdd(entry.Name, _ => new Lazy<IReadOnlyList<SpriteArt>>(() => CreateTableArt(entry))).Value;
    }

    /// <summary>
    /// The floater pictures of an add-on bulb (5.4 <c>ExtraBulbObject_PickUsedEntry</c>): every used animation entry of its
    /// <c>.bul</c> file in entry order, decoded with the faithful 5.4 GIF decoder; a damaged entry shows the WARNING picture.
    /// </summary>
    /// <param name="bulb">An add-on bulb (it has a file).</param>
    /// <returns>The pictures (floater i uses picture i mod count); empty when the bulb has no file or no used entry.</returns>
    public static IReadOnlyList<SpriteArt> ForAddOnBulb(IBulb bulb)
    {
        ArgumentNullException.ThrowIfNull(bulb);
        if (bulb.FilePath is not { } path)
        {
            return [];
        }

        BulFile file = BulFile.Read(path);
        return [.. file.Entries.Where(e => e.Size != 0).Select(e => new SpriteArt($"{bulb.Id}#{e.Index}", DecodeFrames(e.Gif.Span)))];
    }

    private static SpriteArt CreateBalloons()
    {
        Rgba32Image strip = HeritageArt.Balloons;
        int width = strip.Width / BalloonColors;
        return new SpriteArt(
            "balloons",
            [.. Enumerable.Range(0, BalloonColors).Select(i => strip.Crop(RectI.FromXYWH(i * width, 0, width, strip.Height)))]);
    }

    private static IReadOnlyList<SpriteArt> CreateTableArt(SaverAnimationEntry entry)
    {
        if (entry.GifResource is { } resource)
        {
            return [new SpriteArt($"gif:{resource}", DecodeFrames(EmbeddedAssets.ReadAllBytes(HeritageAssets.SaverGif(resource))))];
        }

        return [.. entry.Records.Select(record => new SpriteArt(
            $"builtin:{record.BulbId}:{string.Join(',', record.Cells)}",
            [.. record.Cells.Select(cell => BuiltInBulbs.GetSheetCell(record.BulbId, cell)?.Image ?? HeritageArt.Warning)]))];
    }

    /// <summary>All frames of a GIF with the 5.4 decoder (the saver ignores frame delays), or the WARNING picture.</summary>
    private static IReadOnlyList<Rgba32Image> DecodeFrames(ReadOnlySpan<byte> gif)
    {
        try
        {
            GifAnimation animation = GifDecoder.DecodeClassic(gif);
            return animation.Frames.Count > 0 && !animation.Size.IsEmpty ? animation.Frames : [HeritageArt.Warning];
        }
        catch (InvalidDataException)
        {
            return [HeritageArt.Warning];
        }
    }
}
