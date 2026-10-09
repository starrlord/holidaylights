using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

/// <summary>
/// Every bundled add-on bulb against <c>bul-frames.json.gz</c>: header fields, slot table, categories and every frame of
/// every GIF entry decoded by the faithful decoder (bit-exact with Holiday Lights 5.4).
/// </summary>
public sealed class BundledBulbGoldenTests
{
    [Fact]
    public void Corpus_HasAll1501Files()
    {
        Assert.Equal(1501, BulbGoldens.BulFiles.Count);
        Assert.Equal(1501, Directory.EnumerateFiles(BulbGoldens.BundledBulbsFolder, "*.bul").Count());
    }

    [Fact]
    public void Headers_MatchTheGolden()
    {
        var failures = new ConcurrentBag<string>();
        Parallel.ForEach(BulbGoldens.BulFiles, golden =>
        {
            string name = golden.GetProperty("file").GetString()!;
            try
            {
                CheckHeader(golden, BulbGoldens.ReadBundled(name));
            }
            catch (Exception e) when (e is Xunit.Sdk.XunitException or InvalidOperationException)
            {
                failures.Add($"{name}: {e.Message}");
            }
        });

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(20)));
    }

    [Fact]
    public void EveryFrameOfEveryEntry_MatchesTheGolden()
    {
        var failures = new ConcurrentBag<string>();
        int frames = 0;
        int entries = 0;
        Parallel.ForEach(BulbGoldens.BulFiles, golden =>
        {
            string name = golden.GetProperty("file").GetString()!;
            BulFile file = BulFile.Parse(BulbGoldens.ReadBundled(name), name);
            foreach (JsonElement expected in golden.GetProperty("entries").EnumerateArray())
            {
                int index = expected.GetProperty("index").GetInt32();
                BulGifEntry entry = file.Entries[index];
                Interlocked.Increment(ref entries);
                try
                {
                    GifAnimation animation = GifDecoder.DecodeClassic(entry.Gif.Span);
                    string[] hashes = BulbGoldens.Strings(expected, "sha256");
                    Assert.Equal(expected.GetProperty("gifBytes").GetInt32(), entry.Size);
                    Assert.Equal(expected.GetProperty("width").GetInt32(), animation.Size.Width);
                    Assert.Equal(expected.GetProperty("height").GetInt32(), animation.Size.Height);
                    Assert.Equal(hashes.Length, animation.Frames.Count);
                    Assert.All(animation.DelaysMilliseconds, d => Assert.Equal(0, d));
                    for (int k = 0; k < hashes.Length; k++)
                    {
                        Assert.Equal(hashes[k], GoldenData.RgbaSha256(animation.Frames[k]));
                    }

                    Assert.Equal(expected.GetProperty("constantMask").GetInt32() == 1, MasksAreConstant(animation));
                    Assert.Equal(expected.GetProperty("constantMaskStored").GetInt32() == 1, entry.StoredConstantMask);
                    Interlocked.Add(ref frames, hashes.Length);
                }
                catch (Exception e) when (e is Xunit.Sdk.XunitException or InvalidDataException)
                {
                    failures.Add($"{name}#{index}: {e.Message}");
                }
            }
        });

        Assert.True(failures.IsEmpty, $"{failures.Count} failures:{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(20)));
        Assert.Equal(4511, entries);
        Assert.Equal(18388, frames);
    }

    [Fact]
    public void FirstFrameOnly_StopsAfterFrameZero()
    {
        JsonElement golden = BulbGoldens.BulFiles.First(f => f.GetProperty("file").GetString() == "10thBirthday.bul");
        BulFile file = BulFile.Parse(BulbGoldens.ReadBundled("10thBirthday.bul"));

        GifAnimation first = GifDecoder.DecodeClassic(file.Entries[0].Gif.Span, firstFrameOnly: true);

        Assert.Single(first.Frames);
        Assert.Equal(BulbGoldens.Strings(golden.GetProperty("entries")[0], "sha256")[0], GoldenData.RgbaSha256(first.Frames[0]));
    }

    private static void CheckHeader(JsonElement golden, byte[] data)
    {
        Assert.Equal(golden.GetProperty("bytes").GetInt32(), data.Length);
        Assert.Equal(golden.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        BulFile file = BulFile.Parse(data);
        Assert.False(file.IsDamaged, string.Join("; ", file.Problems));
        Assert.Empty(golden.GetProperty("loaderErrors").EnumerateArray());
        Assert.Equal(golden.GetProperty("bulbId").GetUInt32(), unchecked((uint)file.LegacyId));
        Assert.Equal(golden.GetProperty("name").GetString(), file.Name);
        Assert.Equal(golden.GetProperty("description").GetString(), file.Description);
        Assert.Equal(golden.GetProperty("copyright").GetString(), file.Copyright);
        Assert.Equal(golden.GetProperty("author").GetString(), file.Author);
        Assert.Equal(BulbGoldens.Strings(golden, "categories"), file.Categories);
        Assert.Equal(golden.GetProperty("previewX").GetInt32(), file.PreviewX);
        Assert.Equal(golden.GetProperty("previewY").GetInt32(), file.PreviewY);
        JsonElement slots = golden.GetProperty("slots");
        Assert.Equal(slots.GetProperty("preview").GetInt32(), file.PreviewEntry);
        Assert.Equal(BulbGoldens.Ints(slots.GetProperty("corners")), file.CornerEntries);
        for (int side = 0; side < 4; side++)
        {
            Assert.Equal(BulbGoldens.Ints(slots.GetProperty("sides")[side]), file.SideEntries[side]);
        }

        int[] used = [.. golden.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("index").GetInt32())];
        Assert.Equal(used, file.Entries.Where(e => e.Size != 0).Select(e => e.Index));
        Assert.All(file.Entries.Where(e => e.Size != 0), e => Assert.Equal(e.Size, e.Gif.Length));
        Assert.Equal(1, file.Locked ? 1 : 0);
    }

    private static bool MasksAreConstant(GifAnimation animation)
    {
        uint[] first = animation.Frames[0].Pixels;
        return animation.Frames.Skip(1).All(f => f.Pixels.Select(p => p >> 24).SequenceEqual(first.Select(p => p >> 24)));
    }
}
