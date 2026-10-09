using System.IO;

namespace HolidayLights.Branding;

/// <summary>
/// Writes multi-size Windows icon files. Frames smaller than 256 px are stored as 32-bit DIBs with an AND mask (the
/// format every Windows API and WPF decoder reads); the 256 px frame is stored PNG-compressed (Windows Vista and later).
/// </summary>
internal static class IcoWriter
{
    private const int PngThreshold = 256;

    /// <summary>Writes the frames, smallest first, to an <c>.ico</c> file.</summary>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="frames">Square frames, each of a different size.</param>
    public static void Write(string path, IReadOnlyCollection<PixelImage> frames)
    {
        PixelImage[] ordered = [.. frames.OrderBy(f => f.Width)];
        if (ordered.Any(f => f.Width != f.Height || f.Width is < 1 or > 256))
        {
            throw new ArgumentException("Icon frames must be square and at most 256 px.", nameof(frames));
        }

        byte[][] payloads = [.. ordered.Select(f => f.Width >= PngThreshold ? Raster.EncodePng(f) : EncodeDib(f))];

        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)ordered.Length);
            int offset = 6 + 16 * ordered.Length;
            for (int i = 0; i < ordered.Length; i++)
            {
                int size = ordered[i].Width;
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(payloads[i].Length);
                writer.Write(offset);
                offset += payloads[i].Length;
            }

            foreach (byte[] payload in payloads)
            {
                writer.Write(payload);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, memory.ToArray());
    }

    /// <summary>Encodes one frame as BITMAPINFOHEADER + bottom-up BGRA pixels + 1-bpp AND mask.</summary>
    private static byte[] EncodeDib(PixelImage frame)
    {
        int size = frame.Width;
        int maskStride = (size + 31) / 32 * 4;
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write(40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            for (int y = size - 1; y >= 0; y--)
            {
                for (int x = 0; x < size; x++)
                {
                    writer.Write(frame.Pixels[y * size + x]);
                }
            }

            for (int y = size - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < size; x++)
                {
                    if (frame.Pixels[y * size + x] >> 24 == 0)
                    {
                        row[x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }

                writer.Write(row);
            }
        }

        return memory.ToArray();
    }
}
