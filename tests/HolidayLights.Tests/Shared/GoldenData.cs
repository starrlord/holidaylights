using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace HolidayLights.Tests.Shared;

/// <summary>Readers for the golden test data (owner: contracts; conventions in <c>Golden/README.md</c>).</summary>
public static class GoldenData
{
    /// <summary>Parses a golden JSON file (<c>.json</c> or gzip-compressed <c>.json.gz</c>).</summary>
    /// <param name="relativePath">Path relative to <c>Golden</c>.</param>
    /// <returns>The document; dispose it.</returns>
    public static JsonDocument ReadJson(string relativePath)
    {
        string path = TestPaths.Golden(relativePath);
        using FileStream file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        return JsonDocument.Parse(stream);
    }

    /// <summary>
    /// The golden RGBA hash of an image: lowercase hex SHA-256 over <c>width * height * 4</c> bytes, rows top to bottom,
    /// bytes R, G, B, A per pixel, alpha binary (0 or 255) with the colour of transparent pixels forced to 0.
    /// </summary>
    /// <param name="image">A straight-alpha image.</param>
    /// <returns>The hash.</returns>
    public static string RgbaSha256(Rgba32Image image)
    {
        var bytes = new byte[image.Pixels.Length * 4];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            uint p = image.Pixels[i];
            if (Bgra32.A(p) == 0)
            {
                continue;
            }

            bytes[i * 4] = Bgra32.R(p);
            bytes[i * 4 + 1] = Bgra32.G(p);
            bytes[i * 4 + 2] = Bgra32.B(p);
            bytes[i * 4 + 3] = 255;
        }

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
