using HolidayLights.Core.Imaging;

namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// One GIF of a bulb document: the file bytes (stored verbatim in the <c>.bul</c>), their CRC-32/BZIP2 and the facts the
/// writer records beside them. Two instances are equal when their bytes are equal, which
/// is how a bulb stores identical GIFs once. Immutable and thread-safe.
/// </summary>
public sealed class BulbGif : IEquatable<BulbGif>
{
    private readonly byte[] bytes;
    private readonly Lazy<bool> constantMask;

    private BulbGif(byte[] bytes, SizeI size, Lazy<bool> constantMask)
    {
        this.bytes = bytes;
        this.constantMask = constantMask;
        Crc = Crc32Bzip2.Compute(bytes);
        Size = size;
    }

    /// <summary>The GIF file bytes.</summary>
    public ReadOnlyMemory<byte> Bytes => bytes;

    /// <summary>Number of bytes.</summary>
    public int Length => bytes.Length;

    /// <summary>The CRC-32/BZIP2 of the bytes (entry offset +0x08).</summary>
    public uint Crc { get; }

    /// <summary>The logical screen size, which is the size of every frame (empty when the header cannot be read).</summary>
    public SizeI Size { get; }

    /// <summary>
    /// The 5.4 <c>constantMask</c> flag: every frame has the same 1-bit mask as frame 0,
    /// as decoded by the faithful decoder. For a GIF read from a file that the decoder rejects, the flag stored in the file.
    /// </summary>
    public bool HasConstantMask => constantMask.Value;

    /// <summary>
    /// Checks a GIF the way 5.4's "Change..." did (it must end with its trailer and decode with the faithful decoder) and
    /// wraps a copy of it.
    /// </summary>
    /// <param name="gif">The GIF file bytes.</param>
    /// <returns>The GIF.</returns>
    /// <exception cref="InvalidDataException">Holiday Lights 5.4 could not use the GIF ("Cannot Import GIF File").</exception>
    public static BulbGif FromBytes(ReadOnlySpan<byte> gif)
    {
        GifAnimation decoded = GifDecoder.DecodeClassic(gif);
        bool constant = HasSameMaskInEveryFrame(decoded.Frames);
        return new BulbGif(gif.ToArray(), decoded.Size, new Lazy<bool>(() => constant));
    }

    /// <summary>Wraps a GIF entry of a parsed file; its flag is worked out from the pixels when first needed.</summary>
    /// <param name="entry">A used entry (size not 0).</param>
    /// <returns>The GIF.</returns>
    internal static BulbGif FromEntry(BulGifEntry entry)
    {
        byte[] bytes = entry.Gif.ToArray();
        return new BulbGif(bytes, ReadSize(bytes), new Lazy<bool>(() => ComputeConstantMask(bytes, entry.StoredConstantMask)));
    }

    /// <summary>True when both hold the same bytes.</summary>
    /// <param name="other">Another GIF.</param>
    /// <returns>True for equal content.</returns>
    public bool Equals(BulbGif? other) =>
        other is not null && (ReferenceEquals(this, other) || (Crc == other.Crc && bytes.AsSpan().SequenceEqual(other.bytes)));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as BulbGif);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Crc);

    private static bool ComputeConstantMask(byte[] gif, bool stored)
    {
        try
        {
            return HasSameMaskInEveryFrame(GifDecoder.DecodeClassic(gif).Frames);
        }
        catch (InvalidDataException)
        {
            return stored;
        }
    }

    /// <summary>The 5.4 comparison: each frame's opaque/transparent pattern equals frame 0's.</summary>
    private static bool HasSameMaskInEveryFrame(IReadOnlyList<Rgba32Image> frames)
    {
        uint[] first = frames[0].Pixels;
        for (int f = 1; f < frames.Count; f++)
        {
            uint[] pixels = frames[f].Pixels;
            for (int i = 0; i < first.Length; i++)
            {
                if ((first[i] >> 24 == 0) != (pixels[i] >> 24 == 0))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static SizeI ReadSize(byte[] gif)
    {
        try
        {
            return GifDecoder.ReadLogicalScreenSize(gif);
        }
        catch (InvalidDataException)
        {
            return default;
        }
    }
}
