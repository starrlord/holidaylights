using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>The outcome of reading the files chosen with "Change..." or dropped on the preview.</summary>
/// <param name="Gif">The animation, or null when the files cannot be used.</param>
/// <param name="Error">Why not ("Cannot Import GIF File: A problem occurred ..."), or null.</param>
internal sealed record PictureImportResult(BulbGif? Gif, string? Error)
{
    /// <summary>A failure with a title and a sentence, shown as one InfoBar message.</summary>
    /// <param name="title">E.g. "Cannot Import GIF File".</param>
    /// <param name="text">E.g. "A problem occurred when importing the GIF file. It cannot be used with this bulb.".</param>
    /// <returns>The result.</returns>
    public static PictureImportResult Failed(string title, string text) =>
        new(null, string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.ErrorMessageFormat, title, text));
}

/// <summary>
/// Turns chosen files into a bulb animation (PRODUCT-SPEC 3.3.1 "Change..."): one GIF, checked like 5.4 did (it must end
/// with its trailer and decode with the faithful decoder), or one or more PNG pictures of the same size that become the
/// frames of a new GIF in file-name order (a still picture for one file).
/// </summary>
/// <remarks>Reads files and decodes pictures: call it on the thread pool.</remarks>
internal static class PictureImport
{
    /// <summary>The largest PNG picture used for a bulb, in pixels per side.</summary>
    public const int MaxPictureSide = 1024;

    /// <summary>The largest GIF file used for a bulb, in megabytes.</summary>
    public const int MaxGifMegabytes = 16;

    private const string GifExtension = ".gif";
    private const string PngExtension = ".png";

    /// <summary>Reads the files.</summary>
    /// <param name="paths">The chosen files (at least one).</param>
    /// <returns>The animation or the reason it cannot be used.</returns>
    public static PictureImportResult Load(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string[] gifs = [.. paths.Where(p => HasExtension(p, GifExtension))];
        string[] pngs = [.. paths.Where(p => HasExtension(p, PngExtension))];
        if (paths.Count == 0 || gifs.Length + pngs.Length < paths.Count)
        {
            return PictureImportResult.Failed(BulbFactoryStrings.ProblemWithFile, BulbFactoryStrings.UnsupportedPicture);
        }

        if (gifs.Length == 1 && pngs.Length == 0)
        {
            return LoadGif(gifs[0]);
        }

        return gifs.Length == 0
            ? LoadPngFrames([.. pngs.Order(NaturalFileNameComparer.Instance)])
            : PictureImportResult.Failed(BulbFactoryStrings.ProblemWithFile, BulbFactoryStrings.ChooseOneAnimation);
    }

    private static PictureImportResult LoadGif(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxGifMegabytes * 1024L * 1024L)
            {
                return PictureImportResult.Failed(
                    BulbFactoryStrings.CannotImportGif,
                    string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.GifTooBigFormat, MaxGifMegabytes));
            }

            return new PictureImportResult(BulbGif.FromBytes(File.ReadAllBytes(path)), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return PictureImportResult.Failed(BulbFactoryStrings.CannotImportGif, BulbFactoryStrings.GifProblem);
        }
    }

    private static PictureImportResult LoadPngFrames(IReadOnlyList<string> paths)
    {
        var frames = new List<Rgba32Image>(paths.Count);
        try
        {
            foreach (string path in paths)
            {
                if (ReadPng(path) is not { } frame)
                {
                    return PictureImportResult.Failed(
                        BulbFactoryStrings.CannotImportPicture,
                        string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.PictureTooBigFormat, MaxPictureSide));
                }

                frames.Add(frame);
            }

            if (frames.Any(f => f.Size != frames[0].Size))
            {
                return PictureImportResult.Failed(BulbFactoryStrings.CannotImportPicture, BulbFactoryStrings.PicturesDifferInSize);
            }

            return new PictureImportResult(BulbGif.FromBytes(GifEncoder.Encode(frames)), null);
        }
        catch (Exception e) when (IsPictureProblem(e))
        {
            return PictureImportResult.Failed(BulbFactoryStrings.CannotImportPicture, BulbFactoryStrings.PictureProblem);
        }
    }

    /// <summary>Decodes a PNG (Windows Imaging) into straight-alpha pixels; null when it is larger than <see cref="MaxPictureSide"/>.</summary>
    private static Rgba32Image? ReadPng(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        BitmapFrame source = decoder.Frames[0];
        if (source.PixelWidth > MaxPictureSide || source.PixelHeight > MaxPictureSide)
        {
            return null;
        }

        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new uint[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return new Rgba32Image(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    /// <summary>What reading an unusable picture can throw (Windows Imaging reports damaged files in several ways).</summary>
    private static bool IsPictureProblem(Exception e) =>
        e is IOException or UnauthorizedAccessException or FormatException or NotSupportedException or ArgumentException
            or InvalidOperationException or InvalidDataException or System.Runtime.InteropServices.ExternalException;

    private static bool HasExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Orders file names the way Explorer does: digit runs compare as numbers ("frame2.png" before "frame10.png").</summary>
internal sealed class NaturalFileNameComparer : IComparer<string>
{
    /// <summary>The comparer.</summary>
    public static NaturalFileNameComparer Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        int i = 0;
        int j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i;
                int startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                int byNumber = CompareNumbers(x.AsSpan(startX, i - startX), y.AsSpan(startY, j - startY));
                if (byNumber != 0)
                {
                    return byNumber;
                }
            }
            else
            {
                int byText = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (byText != 0)
                {
                    return byText;
                }

                i++;
                j++;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    /// <summary>Compares two digit runs by value (leading zeros ignored; a longer run of significant digits is larger).</summary>
    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.SequenceCompareTo(b);
    }
}
