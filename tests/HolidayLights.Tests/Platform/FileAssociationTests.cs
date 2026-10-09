using HolidayLights.Platform.Integration;
using HolidayLights.Tests.Shared;
using Microsoft.Win32;

namespace HolidayLights.Tests.Platform;

/// <summary>Runs against <see cref="TestRegistryRoot"/> with a fake shell notification, never the real file types.</summary>
public sealed class FileAssociationTests : IDisposable
{
    private readonly TestRegistryRoot root = new();
    private readonly TempDataRoot data = new();
    private readonly DataPaths paths;
    private int notifications;

    public FileAssociationTests()
    {
        string install = Path.Combine(data.Root, "Install");
        Directory.CreateDirectory(install);
        paths = DataPaths.ForDataRoot(data.Root, install);
        File.WriteAllText(paths.InstalledExePath, "stand-in for the program");
    }

    public void Dispose()
    {
        try
        {
            root.Dispose();
        }
        finally
        {
            TestFolders.Delete(data);
        }
    }

    [Fact]
    public void Register_WritesTheProgIdTheExtensionAndNotifiesTheShell()
    {
        FileAssociation association = Create();
        Assert.Equal(FileAssociationState.NotRegistered, association.GetState());

        association.Register();

        Assert.Equal("TigerTech.HolidayLights.Bulb", root.Get(FileAssociation.ExtensionKey, ""));
        Assert.Equal("Holiday Lights Bulb", root.Get(FileAssociation.ProgIdKey, ""));
        Assert.Equal(paths.BulbDocumentIconPath, root.Get(FileAssociation.ProgIdKey + @"\DefaultIcon", ""));
        Assert.Equal($"\"{paths.InstalledExePath}\" --open \"%1\"", root.Get(FileAssociation.CommandKey, ""));
        Assert.Equal(RegistryValueKind.None, root.KindOf(FileAssociation.ExtensionKey + @"\OpenWithProgids", "TigerTech.HolidayLights.Bulb"));
        Assert.Equal(1, notifications);
        Assert.Equal(FileAssociationState.Registered, association.GetState());
    }

    [Fact]
    public void AMissingProgram_IsBroken()
    {
        FileAssociation association = Create();
        association.Register();

        File.Delete(paths.InstalledExePath);

        Assert.Equal(FileAssociationState.Broken, association.GetState());
    }

    [Fact]
    public void TheHolidayLights54Registration_IsTakenOver()
    {
        root.Set(FileAssociation.ExtensionKey, "", "HolidayLights.Bulb");
        FileAssociation association = Create();
        Assert.Equal(FileAssociationState.NotRegistered, association.GetState());

        association.Register();

        Assert.Equal("TigerTech.HolidayLights.Bulb", root.Get(FileAssociation.ExtensionKey, ""));
        Assert.Equal(FileAssociationState.Registered, association.GetState());
    }

    [Fact]
    public void AnotherProgramsDefault_IsNeverReplaced()
    {
        root.Set(FileAssociation.ExtensionKey, "", "BulbViewer.Document");
        FileAssociation association = Create();
        Assert.Equal(FileAssociationState.OwnedByAnotherProgram, association.GetState());

        association.Register();

        Assert.Equal("BulbViewer.Document", root.Get(FileAssociation.ExtensionKey, ""));
        Assert.True(root.Exists(FileAssociation.CommandKey));
        Assert.Equal(RegistryValueKind.None, root.KindOf(FileAssociation.ExtensionKey + @"\OpenWithProgids", "TigerTech.HolidayLights.Bulb"));
        Assert.Equal(FileAssociationState.OwnedByAnotherProgram, association.GetState());
    }

    [Theory]
    [InlineData("Applications\\notepad.exe", FileAssociationState.OwnedByAnotherProgram)]
    [InlineData("HolidayLights.Bulb", FileAssociationState.OwnedByAnotherProgram)]
    [InlineData("TigerTech.HolidayLights.Bulb", FileAssociationState.Registered)]
    public void TheUsersOpenWithChoice_Decides(string progId, FileAssociationState expected)
    {
        FileAssociation association = Create();
        association.Register();

        root.Set(FileAssociation.UserChoiceKey, "ProgId", progId);

        Assert.Equal(expected, association.GetState());
    }

    [Fact]
    public void Unregister_RemovesOnlyWhatIsOurs()
    {
        root.Set(FileAssociation.ExtensionKey + @"\OpenWithProgids", "BulbViewer.Document", Array.Empty<byte>(), RegistryValueKind.None);
        FileAssociation association = Create();
        association.Register();

        association.Unregister();

        Assert.False(root.Exists(FileAssociation.ProgIdKey));
        Assert.Null(root.Get(FileAssociation.ExtensionKey, ""));
        Assert.Null(root.Get(FileAssociation.ExtensionKey + @"\OpenWithProgids", "TigerTech.HolidayLights.Bulb"));
        Assert.NotNull(root.Get(FileAssociation.ExtensionKey + @"\OpenWithProgids", "BulbViewer.Document"));
        Assert.Equal(FileAssociationState.NotRegistered, association.GetState());
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Unregister_RemovesTheEmptiedExtensionKey()
    {
        FileAssociation association = Create();
        association.Register();

        association.Unregister();

        Assert.False(root.Exists(FileAssociation.ExtensionKey));
    }

    [Fact]
    public void Unregister_LeavesAnotherProgramsDefault()
    {
        root.Set(FileAssociation.ExtensionKey, "", "BulbViewer.Document");
        FileAssociation association = Create();
        association.Register();

        association.Unregister();

        Assert.Equal("BulbViewer.Document", root.Get(FileAssociation.ExtensionKey, ""));
        Assert.False(root.Exists(FileAssociation.ProgIdKey));
    }

    [Fact]
    public void NoSystemChanges_WritesNothing()
    {
        var log = new RecordingLog();
        var association = new FileAssociation(paths, new AppRuntimeOptions { AllowSystemChanges = false }, log, root.Registry, () => notifications++);

        association.Register();
        association.Unregister();

        Assert.False(root.Exists(FileAssociation.ExtensionKey));
        Assert.False(root.Exists(FileAssociation.ProgIdKey));
        Assert.Equal(0, notifications);
        Assert.Equal(2, log.Entries.Count(e => e.Message.Contains("System changes are turned off", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheRealAssociation_CanBeReadWithoutChangingIt()
    {
        var association = new FileAssociation(paths, new AppRuntimeOptions { AllowSystemChanges = false }, new RecordingLog());

        Assert.True(Enum.IsDefined(association.GetState()));
    }

    private FileAssociation Create() =>
        new(paths, new AppRuntimeOptions(), new RecordingLog(), root.Registry, () => notifications++);
}
