using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Tests.Shared;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

public sealed class GifBulbFactoryTests
{
    [Fact]
    public void Create_GivesThe54Defaults()
    {
        byte[] gif = SolidGif(64, 40, Red);

        BulbDocument document = GifBulbFactory.Create(gif, "Star", "Pat Smith", 2026);

        Assert.Equal("Star", document.Name);
        Assert.Equal("No description is available for this bulb.", document.Description);
        Assert.Equal("Pat Smith", document.Author);
        Assert.Equal("Copyright 2026 Pat Smith", document.Copyright);
        Assert.Empty(document.Categories);
        Assert.Equal(1, document.AnimationCount);
        Assert.All(Enum.GetValues<CellSlot>(), slot => Assert.Equal(gif, document.GetAnimation(slot, 0)!.Value.ToArray()));
        Assert.Equal((16, 4), (document.PreviewX, document.PreviewY));
        BulFile file = BulFile.Parse(BulFileWriter.Encode(document, 1234).Bytes);
        Assert.False(file.IsDamaged);
        Assert.False(file.Locked);
        Assert.Equal(9, file.Entries[0].RefCount);
        Assert.Equal(1, file.Entries.Count(e => e.Size != 0));
    }

    [Fact]
    public void Create_UsesThe54TextsWhenTheAccountHasNoName()
    {
        BulbDocument document = GifBulbFactory.Create(SolidGif(8, 8, Red), "  ", " ", 2026);

        Assert.Equal("New Bulb", document.Name);
        Assert.Equal("No author information is available for this bulb.", document.Author);
        Assert.Equal("No copyright information is available for this bulb.", document.Copyright);
    }

    /// <summary>Review r1 #69: a name nothing of which a bulb file can store never becomes "????????".</summary>
    [Fact]
    public void Create_FallsBackWhenNoCharacterCouldBeStored()
    {
        BulbDocument document = GifBulbFactory.Create(SolidGif(8, 8, Red), "Снеговик", "田中 太郎", 2026);

        Assert.Equal("New Bulb", document.Name);
        Assert.Equal("No author information is available for this bulb.", document.Author);
        Assert.Equal("No copyright information is available for this bulb.", document.Copyright);

        // Partly storable text keeps what can be stored (PRODUCT-SPEC 6.3: the rest becomes "?").
        BulbDocument mixed = GifBulbFactory.Create(SolidGif(8, 8, Red), "Star Звезда", "Pat 田中", 2026);
        Assert.Equal("Star ??????", mixed.Name);
        Assert.Equal("Pat ??", mixed.Author);
    }

    [Fact]
    public void Create_CutsLongNamesTo79Characters()
    {
        BulbDocument document = GifBulbFactory.Create(SolidGif(8, 8, Red), new string('s', 90), new string('a', 90), 2026);

        Assert.Equal(79, document.Name.Length);
        Assert.Equal(79, document.Author.Length);
        Assert.Equal(79, document.Copyright.Length);
    }

    [Fact]
    public void Create_RefusesAGifThe54DecoderRefuses()
    {
        byte[] gif = SolidGif(8, 8, Red);

        Assert.Throws<InvalidDataException>(() => GifBulbFactory.Create(gif.AsMemory(0, gif.Length - 1), "Star", "Pat", 2026));
        Assert.Throws<InvalidDataException>(() => GifBulbFactory.Create("GIF89a"u8.ToArray(), "Star", "Pat", 2026));
    }

    [Fact]
    public void ChooseFileName_Numbers54Style()
    {
        using var root = new TempDataRoot();
        string folder = root.Root;

        string first = GifBulbFactory.ChooseFileName(folder, "Star");
        File.WriteAllBytes(first, []);
        string second = GifBulbFactory.ChooseFileName(folder, "Star");
        File.WriteAllBytes(second, []);

        Assert.Equal(Path.Combine(folder, "Star.bul"), first);
        Assert.Equal(Path.Combine(folder, "Star 1.bul"), second);
        Assert.Equal(Path.Combine(folder, "Star 2.bul"), GifBulbFactory.ChooseFileName(folder, "Star"));
        Assert.Equal(Path.Combine(folder, "A_B_.bul"), GifBulbFactory.ChooseFileName(folder, "A/B?."));
        Assert.Equal(Path.Combine(folder, "New Bulb.bul"), GifBulbFactory.ChooseFileName(folder, " "));
    }

    [Fact]
    public void ChooseFileName_GivesUpAfter999()
    {
        using var root = new TempDataRoot();
        File.WriteAllBytes(Path.Combine(root.Root, "Star.bul"), []);
        for (int n = 1; n <= 999; n++)
        {
            File.WriteAllBytes(Path.Combine(root.Root, $"Star {n}.bul"), []);
        }

        Assert.Throws<IOException>(() => GifBulbFactory.ChooseFileName(root.Root, "Star"));
    }

    [Fact]
    public async Task TheCatalogsGifImport_WritesAnEditableBulbThroughTheFactory()
    {
        using var root = new TempDataRoot();
        DataPaths paths = DataPaths.ForDataRoot(root.Root, Directory.CreateDirectory(Path.Combine(root.Root, "Install")).FullName);
        using var catalog = new BulbCatalog(paths, new InMemorySettingsStore(), new TestHoldingFolder(paths), new RecordingLog(), () => "Pat Smith");
        await catalog.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        string gifPath = Path.Combine(root.Root, "Snow Globe.gif");
        File.WriteAllBytes(gifPath, NumberedGif(3));

        BulbImportResult first = catalog.ImportFile(gifPath);
        BulbImportResult second = catalog.ImportFile(gifPath);

        Assert.Equal(BulbImportOutcome.Added, first.Outcome);
        Assert.Equal("user:Snow Globe", first.BulbId);
        Assert.Equal("user:Snow Globe 1", second.BulbId);
        Assert.True(catalog.TryGetBulb(first.BulbId!, out IBulb? bulb));
        Assert.True(bulb.IsEditable);
        Assert.Equal("Snow Globe", bulb.Name);
        Assert.Equal("Pat Smith", bulb.Author);
        Assert.StartsWith("Copyright ", bulb.Copyright);
    }
}
