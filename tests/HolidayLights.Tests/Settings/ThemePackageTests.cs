using System.IO.Compression;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

public sealed class ThemePackageTests : IDisposable
{
    private readonly TempDataRoot sender = new();
    private readonly TempDataRoot receiver = new();

    public void Dispose()
    {
        sender.Dispose();
        receiver.Dispose();
    }

    [Fact]
    public void ExportThenImport_CarriesTheThemeAndTheUsersOwnFiles()
    {
        Directory.CreateDirectory(sender.Paths.MyBulbsFolder);
        Directory.CreateDirectory(sender.Paths.MyPicturesFolder);
        File.WriteAllText(Path.Combine(sender.Paths.MyBulbsFolder, "Star.bul"), "star bulb");
        File.WriteAllText(Path.Combine(sender.Paths.MyBulbsFolder, "Tree.bul"), "tree bulb");
        File.WriteAllText(Path.Combine(sender.Paths.MyPicturesFolder, "Family.jpg"), "family picture");
        var theme = new ThemeDefinition
        {
            Name = "Grandma's Lights",
            Arrangement = SlotAssignment.Empty.WithEdge(Side.Top, ["user:Star", "addon:Arrow", "user:Gone"]).WithCorner(Corner.TopLeft, "user:Star"),
            Flash = new ThemeFlash { Pattern = FlashPatternId.Twinkle, Interval = 4 },
            Music = new ThemeMusic { EnabledSongs = ["bundled:Jingle Bells.mid", "user:Our Song.mp3"], Mode = PlayMode.Always },
            Saver = new ThemeSaver { Animation = SaverAnimations.ForBulb("user:Tree"), Picture = "user:Family.jpg", Message = "Hi Grandma!" },
        };
        string package = Path.Combine(sender.Root, "Grandma" + ThemePackage.Extension);

        ThemePackage.Export(theme, package, sender.Paths);

        using (ZipArchive zip = ZipFile.OpenRead(package))
        {
            Assert.Equal(["bulbs/Star.bul", "bulbs/Tree.bul", "pictures/Family.jpg", "theme.json"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        }

        var bulbs = new FakeBulbCatalog();
        bulbs.ImportedIds["Star.bul"] = "user:Star (2)";
        var pictures = new FakePictureLibrary(myPictures: receiver.Paths.MyPicturesFolder);

        ThemeDefinition imported = ThemePackage.Import(package, receiver.Paths, bulbs, pictures);

        Assert.Equal("Grandma's Lights", imported.Name);
        Assert.Equal(["user:Star (2)", "addon:Arrow", "user:Gone"], imported.Arrangement!.Top);
        Assert.Equal("user:Star (2)", imported.Arrangement.TopLeft);
        Assert.Equal(SaverAnimations.ForBulb("user:Tree"), imported.Saver!.Animation);
        Assert.Equal("user:Family.jpg", imported.Saver.Picture);
        Assert.Equal("Hi Grandma!", imported.Saver.Message);
        Assert.Equal(theme.Music.EnabledSongs, imported.Music!.EnabledSongs);
        Assert.Equal(2, bulbs.Imported.Count);
        Assert.Equal("family picture", File.ReadAllText(Path.Combine(receiver.Paths.MyPicturesFolder, "Family.jpg")));
        Assert.False(Directory.Exists(Path.Combine(receiver.Paths.CacheFolder, "ThemeImport")) && Directory.EnumerateFileSystemEntries(Path.Combine(receiver.Paths.CacheFolder, "ThemeImport")).Any());
    }

    [Fact]
    public void Import_IgnoresEntriesOutsideTheKnownFolders()
    {
        string package = Path.Combine(sender.Root, "Odd" + ThemePackage.Extension);
        using (ZipArchive zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "theme.json", """{ "schema": "holidaylights.theme/1", "name": "Odd/One" }""");
            Write(zip, "bulbs/../../evil.bul", "x");
            Write(zip, "bulbs/sub/inner.bul", "x");
            Write(zip, "pictures/script.ps1", "x");
            Write(zip, "readme.txt", "x");
        }

        var bulbs = new FakeBulbCatalog();
        var pictures = new FakePictureLibrary(myPictures: receiver.Paths.MyPicturesFolder);

        ThemeDefinition imported = ThemePackage.Import(package, receiver.Paths, bulbs, pictures);

        Assert.Equal("Odd-One", imported.Name);
        Assert.Empty(bulbs.Imported);
        Assert.Empty(pictures.AddedSources);
    }

    [Fact]
    public void Import_AFileThatIsNoTheme_Throws()
    {
        string notZip = Path.Combine(sender.Root, "a.hltheme");
        File.WriteAllText(notZip, "hello");
        string noTheme = Path.Combine(sender.Root, "b.hltheme");
        using (ZipArchive zip = ZipFile.Open(noTheme, ZipArchiveMode.Create))
        {
            Write(zip, "readme.txt", "x");
        }

        string foreign = Path.Combine(sender.Root, "c.hltheme");
        using (ZipArchive zip = ZipFile.Open(foreign, ZipArchiveMode.Create))
        {
            Write(zip, "theme.json", """{ "schema": "other/1" }""");
        }

        string damaged = Path.Combine(sender.Root, "d.hltheme");
        using (ZipArchive zip = ZipFile.Open(damaged, ZipArchiveMode.Create))
        {
            Write(zip, "theme.json", "{ broken");
        }

        foreach (string package in new[] { notZip, noTheme, foreign, damaged })
        {
            Assert.Throws<InvalidDataException>(() => ThemePackage.Import(package, receiver.Paths, new FakeBulbCatalog(), new FakePictureLibrary()));
        }
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
