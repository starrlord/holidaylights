using System.Buffers.Binary;

namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// Writes GIF89a files from pictures, so that art drawn as PNG frames can become a bulb animation (a <c>.bul</c> file
/// stores only GIFs). Owner: bulb-factory.
/// </summary>
/// <remarks>
/// <para>One global colour table serves every frame: all the frames' colours when there are at most 255 (256 without
/// transparency), otherwise 255 colours chosen by median cut. Transparency is 1-bit like all bulb art: alpha below 128 is
/// transparent.</para>
/// <para>Every frame is a complete picture that replaces the previous one (disposal 2, no local colour tables), so the
/// faithful 5.4 decoder and standard viewers show exactly the same frames. Multi-frame GIFs loop forever in viewers;
/// Holiday Lights ignores frame delays and steps with the flash speed.</para>
/// </remarks>
public static class GifEncoder
{
    /// <summary>The frame delay written by default: 0.3 s, Holiday Lights' default flash speed.</summary>
    public const int DefaultDelayCentiseconds = 30;

    /// <summary>The largest width or height a GIF can describe.</summary>
    public const int MaxDimension = ushort.MaxValue;

    private const byte DisposeToBackground = 2;

    /// <summary>Encodes frames of equal size as a GIF.</summary>
    /// <param name="frames">Straight-alpha frames, at least one, all the same size.</param>
    /// <param name="delayCentiseconds">The delay of every frame for other viewers (0-65535).</param>
    /// <returns>The GIF file bytes.</returns>
    /// <exception cref="ArgumentException">There is no frame, the frames differ in size, or a size is 0 or above <see cref="MaxDimension"/>.</exception>
    public static byte[] Encode(IReadOnlyList<Rgba32Image> frames, int delayCentiseconds = DefaultDelayCentiseconds)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegative(delayCentiseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delayCentiseconds, ushort.MaxValue);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A GIF needs at least one frame.", nameof(frames));
        }

        SizeI size = frames[0].Size;
        if (size.Width is < 1 or > MaxDimension || size.Height is < 1 or > MaxDimension)
        {
            throw new ArgumentException($"A GIF frame cannot be {size.Width} x {size.Height} pixels.", nameof(frames));
        }

        if (frames.Any(f => f.Size != size))
        {
            throw new ArgumentException("Every frame of a GIF must have the same size.", nameof(frames));
        }

        GifPalette palette = GifPalette.Build(frames);
        var output = new List<byte>();
        output.AddRange("GIF89a"u8);
        AddShort(output, size.Width);
        AddShort(output, size.Height);
        int bits = palette.TableBits;
        output.Add((byte)(0x80 | (bits - 1) << 4 | (bits - 1)));
        output.Add((byte)Math.Max(palette.TransparentIndex, 0));
        output.Add(0);
        for (int i = 0; i < 1 << bits; i++)
        {
            uint rgb = i < palette.Colors.Count ? palette.Colors[i] : 0;
            output.AddRange([(byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb]);
        }

        if (frames.Count > 1)
        {
            AddLoopForever(output);
        }

        foreach (Rgba32Image frame in frames)
        {
            AddControl(output, palette.TransparentIndex, delayCentiseconds);
            output.Add(0x2C);
            AddShort(output, 0);
            AddShort(output, 0);
            AddShort(output, size.Width);
            AddShort(output, size.Height);
            output.Add(0);
            LzwEncoder.Encode(palette.Map(frame), bits, output);
        }

        output.Add(0x3B);
        return [.. output];
    }

    /// <summary>The graphic control extension: disposal 2, the delay and the transparent index.</summary>
    private static void AddControl(List<byte> output, int transparentIndex, int delayCentiseconds)
    {
        output.AddRange([0x21, 0xF9, 4, (byte)(DisposeToBackground << 2 | (transparentIndex >= 0 ? 1 : 0))]);
        AddShort(output, delayCentiseconds);
        output.AddRange([(byte)Math.Max(transparentIndex, 0), 0]);
    }

    /// <summary>The NETSCAPE2.0 application extension with a loop count of 0 (forever).</summary>
    private static void AddLoopForever(List<byte> output)
    {
        output.AddRange([0x21, 0xFF, 11]);
        output.AddRange("NETSCAPE2.0"u8);
        output.AddRange([3, 1, 0, 0, 0]);
    }

    private static void AddShort(List<byte> output, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        output.AddRange(bytes);
    }
}
