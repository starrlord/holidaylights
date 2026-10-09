using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Abstractions;

[Collection(nameof(EnvironmentCollection))]
public sealed class RuntimeTests
{
    [Fact]
    public void DataPaths_ForDataRootRelocatesEveryUserFolder()
    {
        DataPaths paths = DataPaths.ForDataRoot(@"C:\data", @"C:\install");

        Assert.Equal(@"C:\data\Roaming\settings.json", paths.SettingsFile);
        Assert.Equal(@"C:\data\Roaming\Themes", paths.ThemesFolder);
        Assert.Equal(@"C:\data\Local\Cache\index.json", paths.BulbIndexFile);
        Assert.Equal(@"C:\data\Local\Logs", paths.LogsFolder);
        Assert.Equal(@"C:\data\Local\Removed", paths.RemovedFolder);
        Assert.Equal(@"C:\data\Documents\Bulbs", paths.MyBulbsFolder);
        Assert.Equal(@"C:\data\Documents\Music", paths.MyMusicFolder);
        Assert.Equal(@"C:\data\Documents\Pictures", paths.MyPicturesFolder);
        Assert.Equal(@"C:\install\Content\Bulbs", paths.BundledBulbsFolder);
        Assert.Equal(@"C:\install\Holiday Lights.scr", paths.InstalledScreenSaverPath);
        Assert.Equal(@"C:\data", paths.DataRoot);
    }

    [Fact]
    public void DataPaths_FromEnvironmentHonoursTheDataRootVariable()
    {
        string? saved = Environment.GetEnvironmentVariable(DataPaths.DataRootVariable);
        try
        {
            using var root = new TempDataRoot();
            Environment.SetEnvironmentVariable(DataPaths.DataRootVariable, root.Root);

            DataPaths paths = DataPaths.FromEnvironment();

            Assert.Equal(Path.Combine(root.Root, "Roaming", "settings.json"), paths.SettingsFile);
            Assert.Equal(Path.Combine(root.Root, "Documents", "Bulbs"), paths.MyBulbsFolder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataPaths.DataRootVariable, saved);
        }
    }

    [Fact]
    public void AppRuntimeOptions_ReadTheNoSystemChangesVariable()
    {
        string? saved = Environment.GetEnvironmentVariable(AppRuntimeOptions.NoSystemChangesVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppRuntimeOptions.NoSystemChangesVariable, "1");
            Assert.False(AppRuntimeOptions.FromEnvironment().AllowSystemChanges);

            Environment.SetEnvironmentVariable(AppRuntimeOptions.NoSystemChangesVariable, null);
            Assert.True(AppRuntimeOptions.FromEnvironment().AllowSystemChanges);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppRuntimeOptions.NoSystemChangesVariable, saved);
        }
    }

    [Fact]
    public void EmbeddedAssets_HoldTheBuiltInTableAndEveryHeritageAsset()
    {
        Assert.True(EmbeddedAssets.Exists(EmbeddedAssets.BuiltInTable));
        Assert.True(EmbeddedAssets.Exists(EmbeddedAssets.BuiltInBitmap(1001)));
        Assert.Equal(98, EmbeddedAssets.List("builtin/bmp/").Count);

        string[] heritage =
        [
            HeritageAssets.About, HeritageAssets.AboutFlash1, HeritageAssets.AboutFlash2, HeritageAssets.Warning,
            HeritageAssets.WarningMask, HeritageAssets.Dither, HeritageAssets.Flake, HeritageAssets.FlakeMask,
            HeritageAssets.Balloon, HeritageAssets.BalloonMask, HeritageAssets.BalloonSmall, HeritageAssets.BalloonSmallMask,
            HeritageAssets.SaverAnimationTable, HeritageAssets.HelpBanner, HeritageAssets.Icon54, HeritageAssets.DocumentIcon54,
            .. Enumerable.Range(3100, 6).Select(HeritageAssets.SaverGif),
        ];
        Assert.All(heritage, name => Assert.True(EmbeddedAssets.Exists(name), name));
        Assert.Equal((byte)'B', EmbeddedAssets.ReadAllBytes(HeritageAssets.About)[0]);
        Assert.Throws<FileNotFoundException>(() => EmbeddedAssets.Open("heritage/missing.bmp"));
    }
}
