using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

/// <summary>Damaged input is reported, never a crash: corrupted and truncated copies of real files.</summary>
public sealed class MalformedInputTests
{
    private static readonly string[] Samples = ["10thBirthday.bul", "BunnyWithFlower.bul", "Butterfly5.bul", "Gemstones.bul", "Psyduck.bul"];

    [Fact]
    public void CorruptedBulbFiles_AreParsedAndDrawnWithoutExceptions()
    {
        var random = new Random(20261008);
        foreach (string sample in Samples)
        {
            byte[] original = BulbGoldens.ReadBundled(sample);
            for (int round = 0; round < 60; round++)
            {
                byte[] data = Corrupt(original, random);
                BulFile file = BulFile.Parse(data);
                if (file.IsDamaged)
                {
                    Assert.Throws<ArgumentException>(() => AddOnBulbs.FromFile(file, "user:Fuzz", BulbOrigin.UserAddOn, "fuzz"));
                    continue;
                }

                IBulb bulb = AddOnBulbs.FromFile(file, "user:Fuzz", BulbOrigin.UserAddOn, $"fuzz-{sample}-{round}");
                foreach (CellSlot slot in Enum.GetValues<CellSlot>())
                {
                    BulbCell cell = bulb.GetCell(slot, round, round);
                    Assert.Equal(cell.IsPlaceholder ? new SizeI(32, 32) : bulb.GetCellSize(slot, round, round), cell.Image.Size);
                }

                Assert.True(bulb.GetPhaseCount(Side.Left) >= 1);
                _ = bulb.Description;
                _ = bulb.PreviewWindow;
                _ = file.ComputeContentIdentity();
            }
        }
    }

    [Fact]
    public void CorruptedGifs_FailOnlyWithInvalidDataException()
    {
        var random = new Random(8);
        foreach (string sample in Samples)
        {
            BulFile file = BulFile.Parse(BulbGoldens.ReadBundled(sample));
            foreach (BulGifEntry entry in file.Entries.Where(e => e.Size > 0).Take(3))
            {
                byte[] gif = entry.Gif.ToArray();
                for (int round = 0; round < 40; round++)
                {
                    byte[] data = Corrupt(gif, random);
                    DecodeOrReject(() => GifDecoder.DecodeClassic(data));
                    DecodeOrReject(() => GifDecoder.DecodeStandard(data));
                }

                for (int length = 0; length < gif.Length; length += Math.Max(1, gif.Length / 50))
                {
                    byte[] cut = gif.AsSpan(0, length).ToArray();
                    DecodeOrReject(() => GifDecoder.DecodeClassic(cut));
                    DecodeOrReject(() => GifDecoder.DecodeStandard(cut));
                }
            }
        }
    }

    [Fact]
    public void CorruptedBitmaps_FailOnlyWithInvalidDataException()
    {
        var random = new Random(1993);
        byte[][] bitmaps =
        [
            EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(1001)),
            EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(1002)),
            EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(1081)),
            File.ReadAllBytes(Path.Combine(TestPaths.ContentFolder, "Pictures", "Hearts.BMP")),
        ];
        foreach (byte[] bitmap in bitmaps)
        {
            for (int round = 0; round < 80; round++)
            {
                byte[] data = Corrupt(bitmap, random);
                DecodeOrReject(() => BmpDecoder.Decode(data));
                DecodeOrReject(() => BmpDecoder.DecodePicture(data));
                DecodeOrReject(() => BmpDecoder.DecodeMasked(data, bitmaps[1]));
            }
        }
    }

    /// <summary>Overwrites a few bytes (mostly in the headers), sometimes truncates, sometimes appends.</summary>
    private static byte[] Corrupt(byte[] original, Random random)
    {
        byte[] data = (byte[])original.Clone();
        int changes = random.Next(1, 6);
        for (int i = 0; i < changes && data.Length > 0; i++)
        {
            int at = random.Next(2) == 0 ? random.Next(Math.Min(data.Length, 0x600)) : random.Next(data.Length);
            data[at] = (byte)random.Next(256);
        }

        return random.Next(6) switch
        {
            0 => data.AsSpan(0, random.Next(data.Length + 1)).ToArray(),
            1 => [.. data, .. Enumerable.Range(0, random.Next(1, 40)).Select(_ => (byte)random.Next(256))],
            _ => data,
        };
    }

    private static void DecodeOrReject(Func<object> decode)
    {
        try
        {
            decode();
        }
        catch (InvalidDataException)
        {
            // Rejected as damaged: fine.
        }
    }
}
